// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace IslandGateway.ServiceFabricIntegration
{
    /// <summary>
    /// Unit testable wrapper of a Service Fabric Partition.
    /// </summary>
    public record PartitionWrapper
    {
        /// <summary>
        /// Partition id.
        /// </summary>
        public Guid PartitionId { get; init; }
    }
}
