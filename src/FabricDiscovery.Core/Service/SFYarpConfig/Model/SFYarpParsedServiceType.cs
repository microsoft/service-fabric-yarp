// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Yarp.ServiceFabric.FabricDiscovery.Topology;

namespace Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig
{
    internal class SFYarpParsedServiceType
    {
        public TimeSpan LastUsed { get; set; }
        public DiscoveredServiceType FabricServiceType { get; set; }
        public IReadOnlyDictionary<string, string> RawLabels { get; set; }
    }
}
