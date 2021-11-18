// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    internal record DiscoveredPartition
    {
        public DiscoveredPartition(PartitionWrapper partition, IReadOnlyList<DiscoveredReplica> replicas)
        {
            this.Partition = partition ?? throw new ArgumentNullException(nameof(partition));
            this.Replicas = replicas ?? throw new ArgumentNullException(nameof(replicas));
        }

        public PartitionWrapper Partition { get; }
        public IReadOnlyList<DiscoveredReplica> Replicas { get; }
    }
}
