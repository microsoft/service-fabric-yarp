// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
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
