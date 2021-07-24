// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.ServiceFabricIntegration;

namespace IslandGateway.FabricDiscovery.Topology
{
    /// <summary>
    /// Provides methods to discover Service Fabric App Types and their components (e.g. Service Types).
    /// </summary>
    internal interface IAppTypeDiscoveryManager
    {
        /// <summary>
        /// Gets AppType names for apptypes that were discovered and deemed interesting
        /// by <see cref="TopologyDiscoveryFilter"/>.
        /// </summary>
        IList<ApplicationTypeNameKey> GetInterestingAppTypeNames();

        /// <summary>
        /// Re-discovers AppTypes from Service Fabric.
        /// </summary>
        Task Refresh(CancellationToken cancellation);

        /// <summary>
        /// Gets information about an App Type from cache, if available, or else from Service Fabric.
        /// </summary>
        Task<DiscoveredAppType> GetAppTypeInfo(ApplicationTypeNameKey appTypeName, ApplicationTypeVersionKey appTypeVersion, CancellationToken cancellation);
    }
}