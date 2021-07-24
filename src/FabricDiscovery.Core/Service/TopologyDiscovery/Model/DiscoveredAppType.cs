// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using IslandGateway.ServiceFabricIntegration;

namespace IslandGateway.FabricDiscovery.Topology
{
    internal record DiscoveredAppType
    {
        public DiscoveredAppType(ApplicationTypeWrapper appType)
        {
            this.AppType = appType ?? throw new ArgumentNullException(nameof(appType));
        }

        public ApplicationTypeWrapper AppType { get; }
    }
}
