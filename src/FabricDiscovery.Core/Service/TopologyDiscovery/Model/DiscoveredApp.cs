// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using IslandGateway.ServiceFabricIntegration;

namespace IslandGateway.FabricDiscovery.Topology
{
    internal record DiscoveredApp
    {
        public DiscoveredApp(ApplicationWrapper application)
        {
            this.Application = application ?? throw new ArgumentNullException(nameof(application));
        }

        public ApplicationWrapper Application { get; }
    }
}
