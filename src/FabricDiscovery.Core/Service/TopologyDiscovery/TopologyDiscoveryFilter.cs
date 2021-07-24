// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using IslandGateway.ServiceFabricIntegration;

namespace IslandGateway.FabricDiscovery.Topology
{
    /// <summary>
    /// Allows filtering what elements of the Service Fabric cluster are discovered.
    /// This helps avoid discovering entire subtrees that may be uninteresting for the consuming application.
    /// </summary>
    internal class TopologyDiscoveryFilter
    {
        /// <summary>
        /// Whether to discover the topology subtree spanning from the referenced <paramref name="appType"/>.
        /// An AppType subtree includes all ServiceTypes of the specified AppType version.
        /// </summary>
        public virtual bool ShouldDiscoverAppType(ApplicationTypeWrapper appType)
        {
            _ = appType ?? throw new ArgumentNullException(nameof(appType));

            return true;
        }

        /// <summary>
        /// Whether to discover the topology subtree spanning from the referenced <paramref name="app"/>.
        /// An App subtree includes services in that app (but only services that belong to ServiceTypes selected for discovery,
        /// see <see cref="ShouldDiscoverServicesOfServiceType"/>.
        /// </summary>
        public virtual bool ShouldDiscoverApp(DiscoveredAppTypeEx appType, DiscoveredApp app)
        {
            _ = app ?? throw new ArgumentNullException(nameof(app));

            return true;
        }

        /// <summary>
        /// Whether to discover the topology subtree spanning from the referenced <paramref name="serviceType"/>.
        /// A ServiceType subtree includes services of this ServiceType in apps that were selected for discovery,
        /// see <see cref="ShouldDiscoverApp"/>.
        /// </summary>
        public virtual bool ShouldDiscoverServicesOfServiceType(DiscoveredApp app, DiscoveredServiceType serviceType)
        {
            _ = app ?? throw new ArgumentNullException(nameof(app));
            _ = serviceType ?? throw new ArgumentNullException(nameof(serviceType));

            return true;
        }

        /// <summary>
        /// Whether to discover the topology subtree spanning from the referenced <paramref name="service"/>.
        /// A Service subtree includes its set of partitions and replicas.
        /// </summary>
        public virtual bool ShouldDiscoverService(DiscoveredApp app, DiscoveredService service)
        {
            _ = app ?? throw new ArgumentNullException(nameof(app));
            _ = service ?? throw new ArgumentNullException(nameof(service));

            return true;
        }
    }
}
