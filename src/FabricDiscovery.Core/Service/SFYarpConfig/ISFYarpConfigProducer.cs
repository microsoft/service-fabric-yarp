// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig
{
    /// <summary>
    /// Provides a method to produce Island Gateway configurations from <see cref="SFYarpBackendService"/>'s.
    /// </summary>
    internal interface ISFYarpConfigProducer
    {
        /// <summary>
        /// Produces Island Gateway configurations as a pure function from the provided <paramref name="backendServices"/>.
        /// </summary>
        (List<ClusterConfig> Clusters, List<RouteConfig> Routes) ProduceConfig(IReadOnlyList<SFYarpBackendService> backendServices);
    }
}