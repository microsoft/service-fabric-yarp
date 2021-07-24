// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric.Query;
using FluentAssertions;
using IslandGateway.ServiceFabricIntegration;
using Moq;
using Xunit;

namespace IslandGateway.FabricDiscovery.Topology.Tests
{
    public class ServiceDiscoveryManagerExtensionsTests
    {
        [Fact]
        public void CountTotalElements_Scenario1()
        {
            // Arrange
            var manager = SetupScenario1();

            // Act
            var actual = manager.CountTotalElements();

            // Assert
            actual.Should().Be(7);
        }

        // App fabric:/App1               1
        //   Service fabric:/App1/Svc1    1
        //     Partition                  1
        //        Replicas                3
        // App fabric:/App2               1 (subtree not discovered)
        // ----------------------------------
        // Expected total count:          7
        private static IServiceDiscoveryManager SetupScenario1()
        {
            var appType1 = new ApplicationTypeWrapper
            {
                ApplicationTypeName = new ApplicationTypeNameKey("AppTypeName1"),
                ApplicationTypeVersion = new ApplicationTypeVersionKey("AppType1Version1"),
                DefaultParameters = new Dictionary<string, string>(),
                Status = ApplicationTypeStatus.Available,
            };
            var serviceType1 = new ServiceTypeWrapper
            {
                ServiceTypeName = new ServiceTypeNameKey("ServiceType1"),
                ServiceManifestVersion = "ManifestVersion1",
            };
            var app1 = new ApplicationWrapper
            {
                ApplicationName = new ApplicationNameKey(new Uri("fabric:/App1")),
                ApplicationTypeName = appType1.ApplicationTypeName,
                ApplicationTypeVersion = appType1.ApplicationTypeVersion,
                ApplicationParameters = new Dictionary<string, string>(),
            };
            var service1 = new ServiceWrapper
            {
                ServiceName = new ServiceNameKey(new Uri("fabric:/App1/Svc1")),
                ServiceTypeName = serviceType1.ServiceTypeName,
                ServiceManifestVersion = serviceType1.ServiceManifestVersion,
                ServiceKind = ServiceKind.Stateless,
                ServiceStatus = ServiceStatus.Active,
            };

            var discoveredAppType1 = new DiscoveredAppTypeEx(
                new DiscoveredAppType(appType1),
                new Dictionary<ServiceTypeNameKey, DiscoveredServiceType>
                {
                    { serviceType1.ServiceTypeName, new DiscoveredServiceType(serviceType1) },
                });
            var discoveredServiceType1 = new DiscoveredServiceType(serviceType1);

            var discoveredService1 = new DiscoveredService(discoveredServiceType1, service1);
            var discoveredServiceEx1 = new DiscoveredServiceEx(
                discoveredService1,
                new[]
                {
                    new DiscoveredPartition(
                        new PartitionWrapper { PartitionId = Guid.NewGuid() },
                        new[]
                        {
                            new DiscoveredReplica(new ReplicaWrapper { Id = 1 }),
                            new DiscoveredReplica(new ReplicaWrapper { Id = 2 }),
                            new DiscoveredReplica(new ReplicaWrapper { Id = 3 }),
                        }),
                });
            var discoveredApp1 = new DiscoveredAppEx(
                        new DiscoveredApp(app1),
                        discoveredAppType1,
                        new Dictionary<ServiceNameKey, DiscoveredService>()
                        {
                            { service1.ServiceName, discoveredServiceEx1 },
                        });

            var app2 = new ApplicationWrapper
            {
                ApplicationName = new ApplicationNameKey(new Uri("fabric:/App2")),
                ApplicationTypeName = appType1.ApplicationTypeName,
                ApplicationTypeVersion = appType1.ApplicationTypeVersion,
                ApplicationParameters = new Dictionary<string, string>(),
            };
            var discoveredApp2 = new DiscoveredApp(app2);

            var discoveredApps = new Dictionary<ApplicationNameKey, DiscoveredApp>
            {
                { app1.ApplicationName, discoveredApp1 },
                { app2.ApplicationName, discoveredApp2 },
            };

            var result = new Mock<IServiceDiscoveryManager>();
            result.SetupGet(r => r.DiscoveredApps).Returns(discoveredApps);
            return result.Object;
        }
    }
}