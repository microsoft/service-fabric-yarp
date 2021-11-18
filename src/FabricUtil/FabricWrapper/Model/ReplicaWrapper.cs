// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Fabric;
using System.Fabric.Health;
using System.Fabric.Query;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// Unit testable wrapper of a Service Fabric Replica.
    /// </summary>
    public record ReplicaWrapper
    {
        /// <summary>
        /// Replica id.
        /// </summary>
        public long Id { get; init; }

        /// <summary>
        /// Replica address.
        /// </summary>
        public string ReplicaAddress { get; init; }

        /// <summary>
        /// Replica role.
        /// </summary>
        public ReplicaRole? Role { get; init; }

        /// <summary>
        /// Replica health.
        /// </summary>
        public HealthState HealthState { get; init; }

        /// <summary>
        /// Replica status.
        /// </summary>
        public ServiceReplicaStatus ReplicaStatus { get; init; }

        /// <summary>
        /// Service kind.
        /// </summary>
        public ServiceKind ServiceKind { get; init; }

        /* NOTE: These properties are present in the actual Replica class but excluded from the wrapper. Include if needed.
        public string NodeName { get; }
        public TimeSpan LastInBuildDuration { get; }
        protected internal long LastInBuildDurationInSeconds { get; }
        */
    }
}
