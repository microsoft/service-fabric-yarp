// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    /// <summary>
    /// Provides methods to produce high level abstractions from Service Fabric topology,
    /// without keeping any state.
    /// </summary>
    internal interface IServiceDiscoveryHelper
    {
        /// <summary>
        /// Discovers a Service Fabric Application and its components that are deemed interesting,
        /// see <see cref="TopologyDiscoveryFilter"/>.
        /// </summary>
        Task<DiscoveredApp> DiscoverApp(ApplicationWrapper application, CancellationToken cancellation);

        /// <summary>
        /// Discovers a Service Fabric Service and its components that are deemed interesting,
        /// see <see cref="TopologyDiscoveryFilter"/>.
        /// </summary>
        Task<DiscoveredService> DiscoverService(DiscoveredApp app, DiscoveredServiceType serviceType, ServiceWrapper service, CancellationToken cancellation);
    }
}