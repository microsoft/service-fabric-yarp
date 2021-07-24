// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace IslandGateway.FabricDiscovery.Topology
{
    internal record DiscoveredServiceEx : DiscoveredService
    {
        public DiscoveredServiceEx(
            DiscoveredService service,
            IReadOnlyList<DiscoveredPartition> partitions)
            : base(service)
        {
            this.Partitions = partitions ?? throw new ArgumentNullException(nameof(partitions));
        }

        public IReadOnlyList<DiscoveredPartition> Partitions { get; }
    }
}
