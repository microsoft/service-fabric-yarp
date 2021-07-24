// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using IslandGateway.FabricDiscovery.Topology;

namespace IslandGateway.FabricDiscovery.IslandGatewayConfig
{
    /// <summary>
    /// Filters what elements of the Service Fabric cluster are discovered
    /// so that we don't waste efforts discovering items that are irrelevant for Island Gateway.
    /// </summary>
    internal class IslandGatewayTopologyDiscoveryFilter : TopologyDiscoveryFilter
    {
        /// <summary>
        /// We are only interessted in Service Types that declare our ServiceManifest Extension.
        /// </summary>
        public override bool ShouldDiscoverServicesOfServiceType(DiscoveredApp app, DiscoveredServiceType serviceType)
        {
            _ = app ?? throw new ArgumentNullException(nameof(app));
            _ = serviceType ?? throw new ArgumentNullException(nameof(serviceType));

            return serviceType.ServiceType.Extensions?.ContainsKey(Consts.ServiceManifestExtensionName) == true;
        }
    }
}
