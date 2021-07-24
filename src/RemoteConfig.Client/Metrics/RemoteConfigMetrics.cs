// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using IslandGateway.Common.Abstractions.Telemetry;

namespace IslandGateway.RemoteConfig.Metrics
{
    /// <summary>
    /// Metric to measure Service Fabric discovery health.
    /// </summary>
    internal class RemoteConfigMetrics
    {
        private readonly Action<long> remoteConfigHealth;
        private readonly Action<long> remoteConfigFreshnessMs;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteConfigMetrics"/> class.
        /// </summary>
        public RemoteConfigMetrics(IMetricCreator metricCreator)
        {
            _ = metricCreator ?? throw new ArgumentNullException(nameof(metricCreator));

            this.remoteConfigHealth = metricCreator.Create("RemoteConfigHealth");
            this.remoteConfigFreshnessMs = metricCreator.Create("RemoteConfigFreshnessMs");
        }

        /// <summary>
        /// Metric to measure the health of Island Gateway's remote configuration provider.
        /// This measures how Island Gateway is performing.
        /// If this is ever unhealthy, it could be a fault at the remote service, or in Island Gateway itself.
        /// This is a reliable signal to detect service discovery issues early as it verifies the entire stack is healthy.
        /// </summary>
        public void RemoteConfigHealth(long value)
        {
            this.remoteConfigHealth(value);
        }

        /// <summary>
        /// Metric to measure the end-to-end delay of Island Gateway's remote configurations.
        /// This value is computed based on date/time's measures potentially on different machines, and is subject to clock skew.
        /// Negative values, if they occur, are clamped to zero.
        /// </summary>
        public void RemoteConfigFreshnessMs(long value)
        {
            this.remoteConfigFreshnessMs(value);
        }
    }
}