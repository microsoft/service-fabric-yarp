// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Yarp.ServiceFabric.Common;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Abstractions.Time;
using Yarp.ServiceFabric.FabricDiscovery.Util;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    internal class TopologyDiscoveryWorker : CriticalBackgroundService, ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>
    {
        private readonly IAppTypeDiscoveryManager appTypeDiscoveryManager;
        private readonly IServiceDiscoveryManager serviceDiscoveryManager;
        private readonly TopologyFreshnessTracker freshnessTracker;
        private readonly FabricDiscoveryMetrics metrics;
        private readonly IMonotonicTimer timer;
        private readonly FabricDiscoveryOptions options;
        private readonly ILogger<TopologyDiscoveryWorker> logger;
        private readonly IOperationLogger operationLogger;
        private readonly RecurringTask recurringTask;

        private TimeSpan lastFullRefresh;

        private Func<CancellationToken, Task> unsubscribeFunc;

        private Snapshot<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>> snapshot;
        private CancellationTokenSource changeToken;

        public TopologyDiscoveryWorker(
            IAppTypeDiscoveryManager appTypeDiscoveryManager,
            IServiceDiscoveryManager serviceDiscoveryManager,
            TopologyFreshnessTracker freshnessTracker,
            FabricDiscoveryMetrics metrics,
            IMonotonicTimer timer,
            IProcessExiter processExiter,
            IOptions<FabricDiscoveryOptions> options,
            ILogger<TopologyDiscoveryWorker> logger,
            IOperationLogger operationLogger)
            : base(CreateWorkerOptions(options), processExiter, logger)
        {
            this.appTypeDiscoveryManager = appTypeDiscoveryManager ?? throw new ArgumentNullException(nameof(appTypeDiscoveryManager));
            this.serviceDiscoveryManager = serviceDiscoveryManager ?? throw new ArgumentNullException(nameof(serviceDiscoveryManager));
            this.freshnessTracker = freshnessTracker ?? throw new ArgumentNullException(nameof(freshnessTracker));
            this.metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            this.timer = timer ?? throw new ArgumentNullException(nameof(timer));
            this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));

            var iterationDelay = new JitteredTimeSpan(TimeSpan.FromSeconds(1), TimeSpan.Zero);
            this.recurringTask = new RecurringTask(this.RunIterationAsync)
                .WithLogging(logger, operationLogger, "TopologyDiscoveryWorker.Iteration")
                .WithAbortOnConsecutiveFailures(options.Value.AbortAfterConsecutiveFailures)
                .WithIterationDelay(iterationDelay) // Future: react when dirty services are added
                .WithRetryDelay(iterationDelay)
                .WithIterationTimeout(options.Value.AbortAfterTimeoutInSeconds > 0 ? TimeSpan.FromSeconds(options.Value.AbortAfterTimeoutInSeconds) : null);
        }

        private enum FullRefreshOutcome
        {
            Success,
            Failure,
            Canceled,
        }

        public Snapshot<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>> GetSnapshot() => this.snapshot;

        protected override async Task InitAsync(CancellationToken cancellation)
        {
            this.unsubscribeFunc = await this.serviceDiscoveryManager.SubscribeToFabricEvents(cancellation);
            await this.FullRefresh(cancellation);
            this.lastFullRefresh = this.timer.CurrentTime;
            this.freshnessTracker.FullTopology.SetFresh();
            this.freshnessTracker.Topology.SetFresh();
            this.UpdateSnapshot(this.serviceDiscoveryManager.DiscoveredApps);
        }

        protected override Task TeardownAsync(CancellationToken cancellation)
        {
            return this.unsubscribeFunc == null ? Task.CompletedTask : this.unsubscribeFunc(cancellation);
        }

        protected override Task RunAsync(CancellationToken cancellation) => this.recurringTask.RunAsync(cancellation);

        private static WorkerOptions CreateWorkerOptions(IOptions<FabricDiscoveryOptions> options)
        {
            _ = options?.Value ?? throw new ArgumentNullException(nameof(options));
            return new WorkerOptions
            {
                InitializationTimeout = options.Value.AbortAfterTimeoutInSeconds > 0 ? TimeSpan.FromSeconds(options.Value.AbortAfterTimeoutInSeconds) : null,
            };
        }

        private async Task RunIterationAsync(CancellationToken cancellation)
        {
            bool topologyChanged = false;
            if ((this.timer.CurrentTime - this.lastFullRefresh) < TimeSpan.FromSeconds(this.options.FullRefreshPollPeriodInSeconds))
            {
                int numUpdated = await this.serviceDiscoveryManager.RefreshDirtyServices(cancellation);
                if (numUpdated > 0)
                {
                    topologyChanged = true;
                }
            }
            else
            {
                await this.FullRefresh(cancellation);

                // set the last updated time *after* refresh completes, so that, if it fails, we do not update, hence will try again on the next cycle.
                // Note that we deliberately update to the _current_ time and not to the time at the beginning of discovery, as a way to make this a self-limiting process:
                // If Full discovery is running slowly, we will still wait the same amount of time between executions preventing run-away CPU use.
                this.lastFullRefresh = this.timer.CurrentTime;
                topologyChanged = true;
                this.freshnessTracker.FullTopology.SetFresh();
            }

            this.logger.LogInformation($"Service Fabric topology is fresh, changed={topologyChanged}, freshness was {this.freshnessTracker.Topology.Freshness.TotalMilliseconds:F1} ms.");
            this.freshnessTracker.Topology.SetFresh();
            if (topologyChanged)
            {
                this.UpdateSnapshot(this.serviceDiscoveryManager.DiscoveredApps);
            }
        }

        private Task FullRefresh(CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteAsync(
                "TopologyDiscoveryWorker.FullRefresh",
                async () =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    var outcome = FullRefreshOutcome.Failure;
                    try
                    {
                        await this.appTypeDiscoveryManager.Refresh(cancellation);
                        await this.serviceDiscoveryManager.RefreshAll(cancellation);
                        outcome = FullRefreshOutcome.Success;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        outcome = FullRefreshOutcome.Canceled;
                        throw;
                    }
                    finally
                    {
                        string outcomeString = outcome switch
                        {
                            FullRefreshOutcome.Success => "Success",
                            FullRefreshOutcome.Failure => "Failure",
                            FullRefreshOutcome.Canceled => "Canceled",
                            _ => "Unknown",
                        };
                        this.metrics.FabricDiscoveryFullRefreshDuration((long)stopwatch.Elapsed.TotalMilliseconds, outcome: outcomeString);
                        this.metrics.FabricDiscoveryFullRefreshHealth(outcome != FullRefreshOutcome.Failure ? 100 : 0, outcome: outcomeString);
                    }
                });
        }

        private void UpdateSnapshot(IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp> discoveredApps)
        {
            this.logger.LogInformation($"Signaling new SF Topology discovered. {discoveredApps.Count} apps.");

            using var oldToken = this.changeToken;
            this.changeToken = new CancellationTokenSource();
            this.snapshot = new Snapshot<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>(discoveredApps, new CancellationChangeToken(this.changeToken.Token));

            try
            {
                oldToken?.Cancel(throwOnFirstException: false);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error signaling change.");
            }
        }
    }
}
