﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IslandGateway.FabricDiscovery.Topology;

namespace IslandGateway.FabricDiscovery.IslandGatewayConfig
{
    internal class IslandGatewayParsedServiceType
    {
        public TimeSpan LastUsed { get; set; }
        public DiscoveredServiceType FabricServiceType { get; set; }
        public IReadOnlyDictionary<string, string> RawLabels { get; set; }
    }
}
