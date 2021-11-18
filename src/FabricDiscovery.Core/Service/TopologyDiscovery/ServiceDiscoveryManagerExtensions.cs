// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    internal static class ServiceDiscoveryManagerExtensions
    {
        public static int CountTotalElements(this IServiceDiscoveryManager manager)
        {
            _ = manager ?? throw new ArgumentNullException(nameof(manager));

            var discoveredAppsSnapshot = manager.DiscoveredApps;

            int totalElements = discoveredAppsSnapshot.Count;
            foreach (var app in discoveredAppsSnapshot.Values)
            {
                totalElements += CountAppNestedElements(app);
            }

            return totalElements;
        }

        private static int CountAppNestedElements(DiscoveredApp app)
        {
            int count = 0;
            if (app is DiscoveredAppEx appEx)
            {
                count += appEx.Services.Count;
                foreach (var service in appEx.Services.Values)
                {
                    count += CountServiceNestedElements(service);
                }
            }

            return count;
        }

        private static int CountServiceNestedElements(DiscoveredService service)
        {
            int count = 0;
            if (service is DiscoveredServiceEx serviceEx)
            {
                count += serviceEx.Partitions.Count;
                foreach (var partition in serviceEx.Partitions)
                {
                    count += partition.Replicas.Count;
                }
            }

            return count;
        }
    }
}
