// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    internal record DiscoveredAppTypeEx : DiscoveredAppType
    {
        public DiscoveredAppTypeEx(DiscoveredAppType appType, IReadOnlyDictionary<ServiceTypeNameKey, DiscoveredServiceType> serviceTypes)
            : base(appType)
        {
            this.ServiceTypes = serviceTypes ?? throw new ArgumentNullException(nameof(serviceTypes));
        }

        public IReadOnlyDictionary<ServiceTypeNameKey, DiscoveredServiceType> ServiceTypes { get; }
    }
}
