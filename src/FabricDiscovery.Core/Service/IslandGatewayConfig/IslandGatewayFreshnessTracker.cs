// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Yarp.ServiceFabric.FabricDiscovery.Util
{
    internal class IslandGatewayFreshnessTracker
    {
        public FreshnessTracker Properties { get; } = new FreshnessTracker();
    }
}
