// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// Unit testable wrapper of a Service Fabric service notification.
    /// </summary>
    public record ServiceNotificationWrapper
    {
        /// <summary>
        /// Service name.
        /// </summary>
        public ServiceNameKey ServiceName { get; init; }

        /// <summary>
        /// Partition id.
        /// </summary>
        public Guid PartitionId { get; init; }

        /// <summary>
        /// The updated endpoints.
        /// </summary>
        public ICollection<ResolvedServiceEndpoint> Endpoints { get; init; }

        /// <summary>
        /// Details about the partition whose endpoints changed.
        /// </summary>
        public ServicePartitionInformation PartitionInfo { get; init; }
    }
}