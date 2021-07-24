// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using IslandGateway.Common.Abstractions.Telemetry;

namespace IslandGateway.FabricDiscovery.Util
{
    /// <summary>
    /// Metric to measure Service Fabric discovery health.
    /// </summary>
    internal class FabricDiscoveryMetrics
    {
        private readonly Action<long> fabricDiscoveryTopologyFreshness;
        private readonly Action<long> fabricDiscoveryFullTopologyFreshness;
        private readonly Action<long> fabricDiscoveryPropertiesFreshness;
        private readonly Action<long, string> fabricDiscoveryFullRefreshDuration;
        private readonly Action<long, string> fabricDiscoveryFullRefreshHealth;
        private readonly Action<long> fabricDiscoveryNotifications;
        private readonly Action<long> fabricDiscoveryTotalElements;

        /// <summary>
        /// Initializes a new instance of the <see cref="FabricDiscoveryMetrics"/> class.
        /// </summary>
        public FabricDiscoveryMetrics(IMetricCreator metricCreator)
        {
            _ = metricCreator ?? throw new ArgumentNullException(nameof(metricCreator));

            this.fabricDiscoveryTopologyFreshness = metricCreator.Create("FabricDiscoveryTopologyFreshness");
            this.fabricDiscoveryFullTopologyFreshness = metricCreator.Create("FabricDiscoveryFullTopologyFreshness");
            this.fabricDiscoveryPropertiesFreshness = metricCreator.Create("FabricDiscoveryPropertiesFreshness");
            this.fabricDiscoveryFullRefreshDuration = metricCreator.Create("FabricDiscoveryFullRefreshDuration", "outcome");
            this.fabricDiscoveryFullRefreshHealth = metricCreator.Create("FabricDiscoveryFullRefreshHealth", "outcome");
            this.fabricDiscoveryNotifications = metricCreator.Create("FabricDiscoveryNotifications");
            this.fabricDiscoveryTotalElements = metricCreator.Create("FabricDiscoveryTotalElements");
        }

        /// <summary>
        /// Measures how delayed we are in discovering Service Fabric topology.
        /// For example, if Service Fabric API's are running slowly or failing,
        /// this provides reliable early indication of trouble.
        /// </summary>
        public void FabricDiscoveryTopologyFreshness(long value)
        {
            this.fabricDiscoveryTopologyFreshness(value);
        }

        /// <summary>
        /// Measures how delayed we are in fully refreshing the Service Fabric topology.
        /// For example, if Service Fabric API's are running slowly or failing,
        /// this provides reliable early indication of trouble.
        /// </summary>
        public void FabricDiscoveryFullTopologyFreshness(long value)
        {
            this.fabricDiscoveryFullTopologyFreshness(value);
        }

        /// <summary>
        /// Measures how delayed we are in discovering Service Fabric service properties.
        /// </summary>
        public void FabricDiscoveryPropertiesFreshness(long value)
        {
            this.fabricDiscoveryPropertiesFreshness(value);
        }

        /// <summary>
        /// Measures how long full refreshes of Service Fabric topology are taking.
        /// Note that this metric may not be emitted every minute, and it is primarily meant for dashboards, not monitors.
        /// </summary>
        public void FabricDiscoveryFullRefreshDuration(long value, string outcome)
        {
            this.fabricDiscoveryFullRefreshDuration(value, outcome);
        }

        /// <summary>
        /// Indicates whether full topology refreshes are working or not.
        /// </summary>
        public void FabricDiscoveryFullRefreshHealth(long value, string outcome)
        {
            this.fabricDiscoveryFullRefreshHealth(value, outcome);
        }

        /// <summary>
        /// Measures how long full refreshes of Service Fabric topology are taking.
        /// Note that this metric may not be emitted every minute, and it is primarily meant for dashboards, not monitors.
        /// </summary>
        public void FabricDiscoveryNotifications(long value)
        {
            this.fabricDiscoveryNotifications(value);
        }

        /// <summary>
        /// Measures how many total elements (i.e. App Types, Service Types, Apps, Service)
        /// are present in the cluster.
        /// </summary>
        public void FabricDiscoveryTotalElements(long value)
        {
            this.fabricDiscoveryTotalElements(value);
        }
    }
}