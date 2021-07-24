// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.Common.Util;
using IslandGateway.RemoteConfig.Contract;
using IslandGateway.RemoteConfig.Infra;
using IslandGateway.RemoteConfig.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace IslandGateway.RemoteConfig
{
    /// <summary>
    /// The service startup configuration class.
    /// </summary>
    internal class RemoteConfigWorker : CriticalBackgroundService, IProxyConfigProvider
    {
        internal static readonly TimeSpan InitializationTimeout = TimeSpan.FromMinutes(1);

        private readonly ILogger<RemoteConfigWorker> logger;
        private readonly IRemoteConfigFetcherFactory remoteConfigFetcherFactory;
        private readonly RemoteConfigMetrics metrics;
        private readonly RecurringTask recurringTask;

        private IRemoteConfigFetcher remoteConfigFetcher;

        private RemoteConfigSnapshot snapshot;
        private CancellationTokenSource changeToken;

        private string lastSeenETag;
        private DateTimeOffset lastSeenConfigTime = DateTimeOffset.MinValue;

        public RemoteConfigWorker(
            ILogger<RemoteConfigWorker> logger,
            IOperationLogger operationLogger,
            IProcessExiter processExiter,
            IRemoteConfigFetcherFactory remoteConfigFetcherFactory,
            RemoteConfigMetrics metrics,
            IOptions<RemoteConfigDiscoveryOptions> options)
            : base(CreateWorkerOptions(options?.Value), processExiter, logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
            this.remoteConfigFetcherFactory = remoteConfigFetcherFactory ?? throw new ArgumentNullException(nameof(remoteConfigFetcherFactory));
            this.metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

            this.recurringTask = new RecurringTask(this.RunIterationAsync)
                .WithLogging(logger, operationLogger, "RemoteConfigWorker.Iteration")
                .WithAbortOnFailure(); // This worker should never fail, a single failure is grounds to abort

            // Create a blank snapshot to begin with, so that callers can subscribe to the first change event.
            this.Update(null);
        }

        /// <inheritdoc/>
        public IProxyConfig GetConfig() => this.snapshot;

        protected override async Task InitAsync(CancellationToken cancellation)
        {
            this.remoteConfigFetcher = this.remoteConfigFetcherFactory.CreateFetcher();

            // Keep trying until we get a usable config... If this is taking too long,
            // the base class will terminate us as desired -- maybe this instance is bad.
            while (!await this.InitInternalAsync(cancellation))
            {
                // Poor-man's rate limiting. In case something unexpected is happening, avoid hammering the destination services.
                await Task.Delay(200, cancellation);
            }
        }

        protected override async Task RunAsync(CancellationToken cancellation)
        {
            await this.recurringTask.RunAsync(cancellation);
        }

        private static WorkerOptions CreateWorkerOptions(RemoteConfigDiscoveryOptions options)
        {
            _ = options ?? throw new ArgumentNullException(nameof(options));

            return new WorkerOptions
            {
                InitializationTimeout = InitializationTimeout,
            };
        }

        private async Task<bool> InitInternalAsync(CancellationToken cancellation)
        {
            await foreach (var (config, etag) in this.remoteConfigFetcher.GetConfigurationStream(null, cancellation))
            {
                this.Update(config);
                this.lastSeenETag = etag;
                this.lastSeenConfigTime = config.AsOf;

                this.metrics.RemoteConfigHealth(100);
                this.EmitFreshnessMetric();

                return true;
            }

            return false;
        }

        private async Task RunIterationAsync(CancellationToken cancellation)
        {
            bool gotOne = false;
            await foreach (var (config, etag) in this.remoteConfigFetcher.GetConfigurationStream(this.lastSeenETag, cancellation))
            {
                this.Update(config);
                this.lastSeenETag = etag;
                this.lastSeenConfigTime = config.AsOf;

                gotOne = true;
                this.metrics.RemoteConfigHealth(100);
                this.EmitFreshnessMetric();

                if (string.IsNullOrEmpty(etag))
                {
                    // We hit an old FabricDiscovery service that did not support ETag's. Inject an artificial delay otherwise we would be probing it way too frequently.
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
                }
                else
                {
                    // Poor-man's rate limiting. In case something unexpected is happening, avoid hammering the destination service.
                    await Task.Delay(200, cancellation);
                }
            }

            this.EmitFreshnessMetric();
            if (!gotOne)
            {
                // If we were asked to abort, this is fine. Just allow the OCE to bubble out.
                cancellation.ThrowIfCancellationRequested();

                // The async foreach completed early without ever producing a good config, so we have a problem.
                // Note that it would be okay to have the loop end gracefully after we received one or more configs.
                // But in this we failed to get even one. E.g. the destination service is allowed to gracefully restart while we have an active session.

                // While we report an unhealthy metric now, the next iteration (which will be attempted immediately after this)
                // hopefully will give better results.
                this.metrics.RemoteConfigHealth(0);

                // Poor-man's rate limiting. In case something unexpected is happening, avoid hammering the destination service.
                await Task.Delay(200, cancellation);
            }
        }

        private void EmitFreshnessMetric()
        {
            var freshness = (DateTimeOffset.UtcNow - this.lastSeenConfigTime).Clamp(TimeSpan.Zero, TimeSpan.FromDays(1));
            this.metrics.RemoteConfigFreshnessMs((long)freshness.TotalMilliseconds);

            this.logger.LogInformation($"Remote configuration status: etag='{this.lastSeenETag}', freshness={freshness.TotalSeconds:F3} s, config as-of={this.lastSeenConfigTime:o}");
        }

        private void Update(RemoteConfigResponseDto config)
        {
            if (config != null)
            {
                this.logger.LogInformation($"Signaling new Remote config. {config.Clusters.Count} clusters, {config.Routes.Count} routes.");
            }

            using var oldToken = this.changeToken;
            this.changeToken = new CancellationTokenSource();
            this.snapshot = new RemoteConfigSnapshot
            {
                Clusters = config?.Clusters.ToList(),
                Routes = config?.Routes.ToList(),
                ChangeToken = new CancellationChangeToken(this.changeToken.Token),
            };

            try
            {
                oldToken?.Cancel(throwOnFirstException: false);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error signaling change.");
            }
        }

        internal sealed class RemoteConfigSnapshot : IProxyConfig
        {
            public List<RouteConfig> Routes { get; internal set; } = new List<RouteConfig>();

            public List<ClusterConfig> Clusters { get; internal set; } = new List<ClusterConfig>();

            IReadOnlyList<RouteConfig> IProxyConfig.Routes => this.Routes;

            IReadOnlyList<ClusterConfig> IProxyConfig.Clusters => this.Clusters;

            // This field is required.
            public IChangeToken ChangeToken { get; internal set; } = default!;
        }
    }
}
