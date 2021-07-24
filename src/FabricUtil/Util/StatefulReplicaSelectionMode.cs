// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace IslandGateway.ServiceFabricIntegration
{
    /// <summary>
    /// Informs how replicas of stateful services are selected.
    /// For more info, see <see href="https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-concepts-replica-lifecycle"/>.
    /// </summary>
    public enum StatefulReplicaSelectionMode
    {
        /// <summary>
        /// All replicas are eligible.
        /// </summary>
        All = 0,

        /// <summary>
        /// Only the primary replica is eligible.
        /// </summary>
        Primary = 1,

        /// <summary>
        /// Only active secondary replicas are eligible.
        /// </summary>
        ActiveSecondary = 2,
    }
}
