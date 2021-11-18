// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    internal record DiscoveredServiceType
    {
        public DiscoveredServiceType(ServiceTypeWrapper serviceType)
        {
            this.ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        }

        public ServiceTypeWrapper ServiceType { get; }
    }
}
