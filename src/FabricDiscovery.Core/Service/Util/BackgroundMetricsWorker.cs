// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.FabricDiscovery.Topology;
using IslandGateway.FabricDiscovery.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IslandGateway.FabricDiscovery
{
    internal class BackgroundMetricsWorker : CriticalBackgroundService
    {
        private static readonly JitteredTimeSpan ReportInterval = new JitteredTimeSpan(TimeSpan.FromSeconds(45), TimeSpan.Zero);

        private readonly IServiceDiscoveryManager serviceDiscoveryManager;
        private readonly TopologyFreshnessTracker topologyFreshnessTracker;
        private readonly IslandGatewayFreshnessTracker igwFreshnessTracker;
        private readonly FabricDiscoveryMetrics metrics;
        private readonly RecurringTask recurringTask;

        public BackgroundMetricsWorker(
            IServiceDiscoveryManager serviceDiscoveryManager,
            TopologyFreshnessTracker topologyFreshnessTracker,
            IslandGatewayFreshnessTracker igwFreshnessTracker,
            FabricDiscoveryMetrics metrics,
            IProcessExiter processExiter,
            IOptions<FabricDiscoveryOptions> options,
            ILogger<BackgroundMetricsWorker> logger,
            IOperationLogger operationLogger)
            : base(new WorkerOptions(), processExiter, logger)
        {
            this.serviceDiscoveryManager = serviceDiscoveryManager ?? throw new ArgumentNullException(nameof(serviceDiscoveryManager));
            this.topologyFreshnessTracker = topologyFreshnessTracker ?? throw new ArgumentNullException(nameof(topologyFreshnessTracker));
            this.igwFreshnessTracker = igwFreshnessTracker ?? throw new ArgumentNullException(nameof(igwFreshnessTracker));
            this.metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _ = options?.Value ?? throw new ArgumentNullException($"{nameof(options)}.{nameof(options.Value)}");
            _ = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));

            this.recurringTask = new RecurringTask(this.RunIteration)
                .WithLogging(logger, operationLogger, "BackgroundMetricsWorker.Iteration")
                .WithAbortOnFailure() // This worker should never fail, a single failure is grounds to abort
                .WithIterationDelay(ReportInterval)
                .WithIterationTimeout(options.Value.AbortAfterTimeoutInSeconds > 0 ? TimeSpan.FromSeconds(options.Value.AbortAfterTimeoutInSeconds) : null);
        }

        protected override Task RunAsync(CancellationToken cancellation) => this.recurringTask.RunAsync(cancellation);

        private void RunIteration()
        {
            this.metrics.FabricDiscoveryTopologyFreshness((long)this.topologyFreshnessTracker.Topology.Freshness.TotalMilliseconds);
            this.metrics.FabricDiscoveryFullTopologyFreshness((long)this.topologyFreshnessTracker.FullTopology.Freshness.TotalMilliseconds);
            this.metrics.FabricDiscoveryPropertiesFreshness((long)this.igwFreshnessTracker.Properties.Freshness.TotalMilliseconds);

            this.metrics.FabricDiscoveryTotalElements(this.serviceDiscoveryManager.CountTotalElements());
        }
    }
}
