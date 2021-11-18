// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace Yarp.ServiceFabric.FabricDiscovery.IslandGatewayConfig
{
    /// <summary>
    /// Provides a method to produce Island Gateway configurations from <see cref="IslandGatewayBackendService"/>'s.
    /// </summary>
    internal interface IIslandGatewayConfigProducer
    {
        /// <summary>
        /// Produces Island Gateway configurations as a pure function from the provided <paramref name="backendServices"/>.
        /// </summary>
        (List<ClusterConfig> Clusters, List<RouteConfig> Routes) ProduceConfig(IReadOnlyList<IslandGatewayBackendService> backendServices);
    }
}