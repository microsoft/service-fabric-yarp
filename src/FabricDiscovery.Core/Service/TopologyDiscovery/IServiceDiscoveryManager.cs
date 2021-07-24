// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.ServiceFabricIntegration;

namespace IslandGateway.FabricDiscovery.Topology
{
    /// <summary>
    /// Manages discovery of Service Fabric Services and Applications,
    /// including their components such as Partitions and Replicas.
    /// </summary>
    internal interface IServiceDiscoveryManager
    {
        /// <summary>
        /// Gets a snapshot of all apps that have been discovered so far.
        /// The entire tree is immutable, although this property will oint at new trees when something changes.
        /// </summary>
        IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp> DiscoveredApps { get; }

        /// <summary>
        /// Refreshes all services by scanning all *interesting* App Types discovered by
        /// <see cref="IAppTypeDiscoveryManager"/>, see <see cref="IAppTypeDiscoveryManager.GetInterestingAppTypeNames"/>.
        /// </summary>
        Task RefreshAll(CancellationToken cancellation);

        /// <summary>
        /// Refreshes services that have been marked as dirty and need to be refreshed.
        /// Services are marked as dirty in response to Service Fabric endpoint change notifications.
        /// </summary>
        Task<int> RefreshDirtyServices(CancellationToken cancellation);

        /// <summary>
        /// Subscribes to service endpoint change notifications.
        /// </summary>
        /// <returns>Asynchronously returns a function that can be called to unsubscribe.</returns>
        Task<Func<CancellationToken, Task>> SubscribeToFabricEvents(CancellationToken cancellation);
    }
}