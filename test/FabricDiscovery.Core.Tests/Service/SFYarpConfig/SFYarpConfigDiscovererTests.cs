// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Health;
using System.Fabric.Query;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Tests.Common;
using Xunit;
using Xunit.Abstractions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.FabricWrapper;
using Yarp.ServiceFabric.FabricDiscovery.Util;
using Yarp.ServiceFabric.FabricDiscovery.Util.Tests;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig.Tests
{
    public class SFYarpConfigDiscovererTests : TestAutoMockBase
    {
        /* TODO tests
            - Unhealthy replicas are not queried (not implemented yet)
            - Try different ListenerNames in the labels
            - Check that serviceFabricCaller failures don't complete crash
        */
#pragma warning disable SA1309 // Field names should not begin with underscore
        private static readonly Uri _testServiceName = new Uri("fabric:/App1/Svc1");
        private readonly List<HealthReport> _healthReports = new List<HealthReport>();
#pragma warning restore SA1309 // Field names should not begin with underscore

        private readonly Mock<ISnapshotProvider<IReadOnlyList<SFYarpBackendService>>> topologyProvider;
        private readonly Mock<ISFYarpConfigProducer> configProducer;

        public SFYarpConfigDiscovererTests()
        {
            this.Provide<IOperationLogger, NullOperationLogger>();
            this.topologyProvider = this.Mock<ISnapshotProvider<IReadOnlyList<SFYarpBackendService>>>();
            this.configProducer = this.Mock<ISFYarpConfigProducer>();
            this.Provide(Options.Create(new FabricDiscoveryOptions()));
    }

        [Fact]
        public async void ExecuteAsync_NoAppsDiscovered_NoClusters()
        {
            this.Mock_AppsResponse();

            var (routes, clusters) = await this.RunScenarioAsync();

            routes.Should().BeEmpty();
            clusters.Should().BeEmpty();
        }

        [Fact]
        public async void ExecuteAsync_NoExtensions_NoClusters()
        {
            ApplicationWrapper application;
            this.Mock_AppsResponse(application = this.CreateApp_1Service_SingletonPartition_1Replica("MyCoolApp", "MyAwesomeService", out var service, out _, out _));
            this.Mock_ServiceLabels(application, service, new Dictionary<string, string>());

            var (routes, clusters) = await this.RunScenarioAsync();

            routes.Should().BeEmpty();
            clusters.Should().BeEmpty();
            this._healthReports.Should().BeEmpty();
        }

        [Fact]
        public async void ExecuteAsync_GatewayNotEnabled_NoClusters()
        {
            ApplicationWrapper application;
            this.Mock_AppsResponse(application = this.CreateApp_1Service_SingletonPartition_1Replica("MyCoolApp", "MyAwesomeService", out var service, out _, out _));
            this.Mock_ServiceLabels(application, service, new Dictionary<string, string>() { { "Yarp.Enable", "false" } });

            var (routes, clusters) = await this.RunScenarioAsync();

            routes.Should().BeEmpty();
            clusters.Should().BeEmpty();
            this._healthReports.Should().BeEmpty();
        }

        [Fact]
        public async void ExecuteAsync_SingleServiceWithGatewayEnabled_OneClusterFound()
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);
            ApplicationWrapper application, anotherApplication;
            this.Mock_AppsResponse(
                application = this.CreateApp_1StatelessService_2Partition_2ReplicasEach("MyApp", "MYService", out var service, out var replicas, out var partitions),
                anotherApplication = this.CreateApp_1StatelessService_2Partition_2ReplicasEach("AnotherApp", "AnotherService", out var anotherService, out var otherReplicas, out var otherPartitions));
            this.Mock_ServiceLabels(application, service, labels);
            this.Mock_ServiceLabels(anotherApplication, anotherService, new Dictionary<string, string>());
             
            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                ClusterWithDestinations(_testServiceName, labels,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[0], partitions[0]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[1], partitions[0]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[2], partitions[1]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[3], partitions[1])),
            };
            var expectedRoutes = LabelsParserV2.BuildRoutes(_testServiceName, labels);

            routes.Should().BeEquivalentTo(expectedRoutes);
            clusters.Should().BeEquivalentTo(expectedClusters);
            this.AssertServiceHealthReported(service, HealthState.Ok);
            foreach (var replica in replicas)
            {
                this.AssertStatelessServiceInstanceHealthReported(replica, HealthState.Ok);
            }
            this._healthReports.Should().HaveCount(5);
        }

        [Fact]
        public async void ExecuteAsync_SingleServiceWithGatewayEnabledAndActiveHealthCheck()
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId, activeHealthChecks: true);
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1StatelessService_2Partition_2ReplicasEach("MyApp", "MYService", out var service, out var replicas, out var partitions));
            this.Mock_ServiceLabels(application, service, labels);

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                ClusterWithDestinations(_testServiceName, labels,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[0], partitions[0]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[1], partitions[0]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[2], partitions[1]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[3], partitions[1])),
            };
            var expectedRoutes = LabelsParserV2.BuildRoutes(_testServiceName, labels);

            routes.Should().BeEquivalentTo(expectedRoutes);
            clusters.Should().BeEquivalentTo(expectedClusters);
            this.AssertServiceHealthReported(service, HealthState.Ok);
            this._healthReports.Should().HaveCount(1);
        }

        [Fact]
        public async void ExecuteAsync_MultipleServicesWithGatewayEnabled_MultipleClustersFound()
        {
            const string TestClusterIdApp1Sv1 = "MyService123";
            const string TestClusterIdApp1Sv2 = "MyService234";
            const string TestClusterIdApp2Sv3 = "MyService345";
            var labels1 = SFTestHelpers.DummyLabels(TestClusterIdApp1Sv1);
            var labels2 = SFTestHelpers.DummyLabels(TestClusterIdApp1Sv2);
            var labels3 = SFTestHelpers.DummyLabels(TestClusterIdApp2Sv3);
            ApplicationWrapper application1, application2;
            this.Mock_AppsResponse(
                application1 = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MYService", out var service1, out var replica1, out var partition1),
                application2 = this.CreateApp_2StatelessService_SingletonPartition_1Replica("MyApp2", "MyService2", "MyService3", out var service2, out var service3, out var replica2, out var replica3, out var partition2, out var partition3));

            this.Mock_ServiceLabels(application1, service1, labels1);
            this.Mock_ServiceLabels(application2, service2, labels2);
            this.Mock_ServiceLabels(application2, service3, labels3);

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                ClusterWithDestinations(_testServiceName, labels1,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica1, partition1)),
                ClusterWithDestinations(_testServiceName, labels2,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica2, partition2)),
                ClusterWithDestinations(_testServiceName, labels3,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica3, partition3)),
            };
            var expectedRoutes = new List<RouteConfig>();
            expectedRoutes.AddRange(LabelsParserV2.BuildRoutes(_testServiceName, labels1));
            expectedRoutes.AddRange(LabelsParserV2.BuildRoutes(_testServiceName, labels2));
            expectedRoutes.AddRange(LabelsParserV2.BuildRoutes(_testServiceName, labels3));

            clusters.Should().BeEquivalentTo(expectedClusters);
            routes.Should().BeEquivalentTo(expectedRoutes);
            this.AssertServiceHealthReported(service1, HealthState.Ok);
            this.AssertServiceHealthReported(service2, HealthState.Ok);
            this.AssertServiceHealthReported(service3, HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replica1, HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replica2, HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replica3, HealthState.Ok);
            this._healthReports.Should().HaveCount(6);
        }

        [Fact]
        public async void ExecuteAsync_OneServiceWithGatewayEnabledAndOneNotEnabled_OnlyTheOneEnabledFound()
        {
            const string TestClusterIdApp1Sv1 = "MyService123";
            const string TestClusterIdApp2Sv2 = "MyService234";
            var gatewayEnabledLabels = SFTestHelpers.DummyLabels(TestClusterIdApp1Sv1);
            var gatewayNotEnabledLabels = SFTestHelpers.DummyLabels(TestClusterIdApp2Sv2, false);
            ApplicationWrapper application1, application2;
            this.Mock_AppsResponse(
                application1 = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MyService1", out var service1, out var replica1, out var partition1),
                application2 = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp2", "MyService2", out var service2, out var replica2, out var partition2));

            this.Mock_ServiceLabels(application1, service1, gatewayEnabledLabels);
            this.Mock_ServiceLabels(application2, service2, gatewayNotEnabledLabels);

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                ClusterWithDestinations(_testServiceName, gatewayEnabledLabels, SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica1, partition1)),
            };
            var expectedRoutes = LabelsParserV2.BuildRoutes(_testServiceName, gatewayEnabledLabels);

            clusters.Should().BeEquivalentTo(expectedClusters);
            routes.Should().BeEquivalentTo(expectedRoutes);
            this.AssertServiceHealthReported(service1, HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replica1, HealthState.Ok);
            this._healthReports.Should().HaveCount(2);
        }

        [Fact]
        public async void ExecuteAsync_GetLabelsFails_NoClustersAndBadHealthReported()
        {
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MYService", out var service, out var replica1, out _));

            // Mock_ServiceLabelsException(application, service, new ConfigException("foo"));
            var (routes, clusters) = await this.RunScenarioAsync();

            clusters.Should().BeEmpty();
            routes.Should().BeEmpty();

            // this.AssertServiceHealthReported(service, HealthState.Warning, (description) => description.Contains("foo"));
            // this._healthReports.Should().HaveCount(1);
        }

        [Theory]
        [InlineData("Yarp.Backend.HealthCheck.Active.Interval", "not a number")]
        public async void ExecuteAsync_InvalidLabelsForCluster_NoClustersAndBadHealthReported(string keyToOverride, string value)
        {
            var labels = SFTestHelpers.DummyLabels("SomeClusterId");
            labels[keyToOverride] = value;
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MyService", out var service, out var replica, out _));

            this.Mock_ServiceLabels(application, service, labels);

            var (routes, clusters) = await this.RunScenarioAsync();

            clusters.Should().BeEmpty();
            routes.Should().BeEmpty();
            this.AssertServiceHealthReported(service, HealthState.Warning, (description) =>
                description.Contains(keyToOverride)); // Check that the invalid key is mentioned in the description
            this._healthReports.Where(report => report.HealthInformation.HealthState == HealthState.Warning).Should().HaveCount(1);
        }

        [Fact]
        public async void ExecuteAsync_InvalidRouteOrder_NoRoutesAndBadHealthReported()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Enable", "true" },
                { "Yarp.Backend.BackendId", "SomeClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "not a number" },
            };
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MyService", out var service, out var replica, out var partition));

            this.Mock_ServiceLabels(application, service, labels);

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                ClusterWithDestinations(_testServiceName, labels,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica, partition)),
            };
            var expectedRoutes = new List<RouteConfig>();

            clusters.Should().BeEquivalentTo(expectedClusters);
            routes.Should().BeEmpty();
            this.AssertServiceHealthReported(service, HealthState.Warning, (description) =>
                description.Contains("Order")); // Check that the invalid key is mentioned in the description
            this._healthReports.Should().HaveCount(2);
        }

        [Fact]
        public async void ExecuteAsync_InvalidListenerNameForStatefulService_NoEndpointsAndBadHealthReported()
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);
            labels["Yarp.Backend.ServiceFabric.ListenerName"] = "UnexistingListener";
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MyService", out var service, out var replica, out _, serviceKind: ServiceKind.Stateful));

            this.Mock_ServiceLabels(application, service, labels);

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                LabelsParserV2.BuildCluster(_testServiceName, labels, new Dictionary<string, DestinationConfig>()),
            };
            var expectedRoutes = LabelsParserV2.BuildRoutes(_testServiceName, labels);

            clusters.Should().BeEquivalentTo(expectedClusters);
            routes.Should().BeEquivalentTo(expectedRoutes);
            this.AssertServiceHealthReported(service, HealthState.Ok);
            this.AssertStatefulServiceReplicaHealthReported(replica, HealthState.Warning, (description) =>
                description.StartsWith("Could not build service endpoint") &&
                description.Contains("UnexistingListener"));
            this._healthReports.Should().HaveCount(2);
        }

        [Fact]
        public async void ExecuteAsync_InvalidListenerNameForStatelessService_NoEndpointsAndBadHealthReported()
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);
            labels["Yarp.Backend.ServiceFabric.ListenerName"] = "UnexistingListener";
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MyService", out var service, out var replica, out _, serviceKind: ServiceKind.Stateless));

            this.Mock_ServiceLabels(application, service, labels);

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                LabelsParserV2.BuildCluster(_testServiceName, labels, new Dictionary<string, DestinationConfig>()),
            };
            var expectedRoutes = LabelsParserV2.BuildRoutes(_testServiceName, labels);

            clusters.Should().BeEquivalentTo(expectedClusters);
            routes.Should().BeEquivalentTo(expectedRoutes);
            this.AssertServiceHealthReported(service, HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replica, HealthState.Warning, (description) =>
                description.StartsWith("Could not build service endpoint") &&
                description.Contains("UnexistingListener"));
            this._healthReports.Should().HaveCount(2);
        }

        [Fact]
        public async void ExecuteAsync_NotHttpsSchemeForStatelessService_NoEndpointsAndBadHealthReported()
        {
            const string TestClusterId = "MyService123";
            const string ServiceName = "fabric:/MyApp/MyService";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);
            labels["Yarp.Backend.ServiceFabric.ListenerName"] = "ExampleTeamEndpoint";
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MyService", out var service, out var replica, out _, serviceKind: ServiceKind.Stateless));
            var nonHttpAddress = $"http://127.0.0.1/{ServiceName}/0";
            replica.ReplicaAddress = $"{{'Endpoints': {{'ExampleTeamEndpoint': '{nonHttpAddress}' }} }}".Replace("'", "\"");
            this.Mock_ServiceLabels(application, service, labels);

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                LabelsParserV2.BuildCluster(_testServiceName, labels, new Dictionary<string, DestinationConfig>()),
            };
            var expectedRoutes = LabelsParserV2.BuildRoutes(_testServiceName, labels);

            clusters.Should().BeEquivalentTo(expectedClusters);
            routes.Should().BeEquivalentTo(expectedRoutes);
            this.AssertServiceHealthReported(service, HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replica, HealthState.Warning, (description) =>
                description.StartsWith("Could not build service endpoint") &&
                description.Contains("ExampleTeamEndpoint"));
            this._healthReports.Should().HaveCount(2);
        }

        [Fact]
        public async void ExecuteAsync_ValidListenerNameForStatelessService_Work()
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);
            labels["Yarp.Backend.ServiceFabric.ListenerName"] = "ExampleTeamEndpoint";
            labels["Yarp.Backend.HealthCheck.Active.ServiceFabric.ListenerName"] = "ExampleTeamHealthEndpoint";
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MyService", out var service, out var replica, out var partition, serviceKind: ServiceKind.Stateless));
            replica.ReplicaAddress = this.MockReplicaAdressWithListenerName("MyApp", "MyService", new string[] { "ExampleTeamEndpoint", "ExampleTeamHealthEndpoint" });
            this.Mock_ServiceLabels(application, service, labels);

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                ClusterWithDestinations(_testServiceName, labels,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica, partition, "ExampleTeamHealthEndpoint")),
            };
            var expectedRoutes = LabelsParserV2.BuildRoutes(_testServiceName, labels);

            clusters.Should().BeEquivalentTo(expectedClusters);
            routes.Should().BeEquivalentTo(expectedRoutes);
            this.AssertServiceHealthReported(service, HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replica, HealthState.Ok, (description) =>
                description.StartsWith("Successfully built"));
            this._healthReports.Should().HaveCount(2);
        }

        [Fact]
        public async void ExecuteAsync_SomeUnhealthyReplicas_OnlyHealthyReplicasAreUsed()
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1StatelessService_2Partition_2ReplicasEach(
                    "MyApp",
                    "MYService",
                    out var service,
                    out var replicas,
                    out var partitions));
            this.Mock_ServiceLabels(application, service, labels);

            replicas.Add(new ReplicaWrapper { ReplicaStatus = ServiceReplicaStatus.Ready, HealthState = HealthState.Warning }); // Should be used despite Warning health state
            replicas.Add(new ReplicaWrapper { ReplicaStatus = ServiceReplicaStatus.Ready, HealthState = HealthState.Ok }); // Should be used
            replicas.Add(new ReplicaWrapper { ReplicaStatus = ServiceReplicaStatus.Ready, HealthState = HealthState.Error }); // Should be used despite Error health state
            replicas.Add(new ReplicaWrapper { ReplicaStatus = ServiceReplicaStatus.Down, HealthState = HealthState.Ok }); // Should be skipped because of status

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                ClusterWithDestinations(_testServiceName, labels,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[0], partitions[0]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[1], partitions[0]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[2], partitions[1])),
            };
            var expectedRoutes = LabelsParserV2.BuildRoutes(_testServiceName, labels);

            clusters.Should().BeEquivalentTo(expectedClusters);
            routes.Should().BeEquivalentTo(expectedRoutes);
            this.AssertServiceHealthReported(service, HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replicas[0], HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replicas[1], HealthState.Ok);
            this.AssertStatelessServiceInstanceHealthReported(replicas[2], HealthState.Ok);
            this._healthReports.Should().HaveCount(4); // 1 service + 3 replicas = 4 health reports
        }

        [Fact]
        public async void ExecuteAsync_ReplicaHealthReportDisabled_ReplicasHealthIsNotReported()
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);
            ApplicationWrapper application;
            this.Mock_AppsResponse(
                application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MYService", out var service, out var replica, out var partition));
            this.Mock_ServiceLabels(application, service, labels);

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                ClusterWithDestinations(_testServiceName, labels,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica, partition)),
            };
            var expectedRoutes = LabelsParserV2.BuildRoutes(_testServiceName, labels);

            clusters.Should().BeEquivalentTo(expectedClusters);
            routes.Should().BeEquivalentTo(expectedRoutes);
            this.AssertServiceHealthReported(service, HealthState.Ok);
            this._healthReports.Should().HaveCount(1);
        }

        [Theory]
        [InlineData("PrimaryOnly", ReplicaRole.Primary)]
        [InlineData("primaryonly", ReplicaRole.Primary)]
        [InlineData("SecondaryOnly", ReplicaRole.ActiveSecondary)]
        [InlineData("All", ReplicaRole.None)]
        [InlineData("All", ReplicaRole.Unknown)]
        [InlineData("All", ReplicaRole.Primary)]
        [InlineData("All", ReplicaRole.ActiveSecondary)]
        [InlineData("All", null)]
        public async void ExecuteAsync_StatefulService_SelectReplicaWork(string selectionMode, ReplicaRole? replicaRole)
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);
            labels["Yarp.Backend.ServiceFabric.StatefulReplicaSelectionMode"] = selectionMode;
            ApplicationWrapper application;
            this.Mock_AppsResponse(application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MYService", out var service, out var replica, out var partition, serviceKind: ServiceKind.Stateful));
            this.Mock_ServiceLabels(application, service, labels);
            replica.ServiceKind = ServiceKind.Stateful;
            replica.Role = replicaRole;

            var (_, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                ClusterWithDestinations(_testServiceName, labels,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica, partition)),
            };

            clusters.Should().BeEquivalentTo(expectedClusters);
            this._healthReports.Should().HaveCount(2);
        }

        [Theory]
        [InlineData("PrimaryOnly", ReplicaRole.None)]
        [InlineData("PrimaryOnly", ReplicaRole.Unknown)]
        [InlineData("PrimaryOnly", ReplicaRole.ActiveSecondary)]
        [InlineData("PrimaryOnly", null)]
        [InlineData("SecondaryOnly", ReplicaRole.None)]
        [InlineData("SecondaryOnly", ReplicaRole.Unknown)]
        [InlineData("SecondaryOnly", ReplicaRole.Primary)]
        [InlineData("SecondaryOnly", null)]
        public async void ExecuteAsync_StatefulService_SkipReplicaWork(string selectionMode, ReplicaRole? replicaRole)
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);
            labels["Yarp.Backend.ServiceFabric.StatefulReplicaSelectionMode"] = selectionMode;
            ApplicationWrapper application;
            this.Mock_AppsResponse(application = this.CreateApp_1Service_SingletonPartition_1Replica("MyApp", "MYService", out var service, out var replica, out _, serviceKind: ServiceKind.Stateful));
            this.Mock_ServiceLabels(application, service, labels);
            replica.ServiceKind = ServiceKind.Stateful;
            replica.Role = replicaRole;

            var (routes, clusters) = await this.RunScenarioAsync();

            var expectedClusters = new[]
            {
                LabelsParserV2.BuildCluster(_testServiceName, labels, new Dictionary<string, DestinationConfig>()),
            };

            clusters.Should().BeEquivalentTo(expectedClusters);
            this._healthReports.Should().HaveCount(1);
        }

        private static ClusterConfig ClusterWithDestinations(Uri serviceName, Dictionary<string, string> labels,
            params KeyValuePair<string, DestinationConfig>[] destinations)
        {
            var newDestinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var destination in destinations)
            {
                newDestinations.Add(destination.Key, destination.Value);
            }

            return LabelsParserV2.BuildClusters(serviceName, labels, newDestinations);
        }

        private async Task<(IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters)> RunScenarioAsync()
        {
            var worker = this.Create<SFYarpConfigProducer>();
            var result = await worker.ComputeYarpModel();
            /*
            var worker = this.Create<Discoverer>();
            var result = await worker.DiscoverAsync(CancellationToken.None);

            this.Mock<ICachedServiceFabricCaller>().Verify(c => c.CleanUpExpired(), Times.Once);
            */

            return result;
        }

        // Assertion helpers
        private void AssertServiceHealthReported(ServiceWrapper service, HealthState expectedHealthState, Func<string, bool> descriptionCheck = null)
        {
            this.AssertHealthReported(
                expectedHealthState: expectedHealthState,
                descriptionCheck: descriptionCheck,
                extraChecks: report => (report as ServiceHealthReport) != null && (report as ServiceHealthReport).ServiceName == service.ServiceName,
                because: $"health '{expectedHealthState}' for service {service.ServiceName} should be reported");
        }
        private void AssertStatelessServiceInstanceHealthReported(ReplicaWrapper replica, HealthState expectedHealthState, Func<string, bool> descriptionCheck = null)
        {
            // TODO: test helpers don't return the fake partition ID so we can't verify replica.PartitioinId is the correct one. Pending to refactor the fixture helpers.
            this.AssertHealthReported(
                expectedHealthState: expectedHealthState,
                descriptionCheck: descriptionCheck,
                extraChecks: report => (report as StatelessServiceInstanceHealthReport) != null && (report as StatelessServiceInstanceHealthReport).InstanceId == replica.Id,
                because: $"health '{expectedHealthState}' for stateless instance {replica.Id} should be reported");
        }
        private void AssertStatefulServiceReplicaHealthReported(ReplicaWrapper replica, HealthState expectedHealthState, Func<string, bool> descriptionCheck = null)
        {
            // TODO: test helpers don't return the fake partition ID so we can't verify replica.PartitioinId is the correct one. Pending to refactor the fixture helpers.
            this.AssertHealthReported(
                expectedHealthState: expectedHealthState,
                descriptionCheck: descriptionCheck,
                extraChecks: report => (report as StatefulServiceReplicaHealthReport) != null && (report as StatefulServiceReplicaHealthReport).ReplicaId == replica.Id,
                because: $"health '{expectedHealthState}' for stateful replica {replica.Id} should be reported");
        }
        private void AssertHealthReported(
            HealthState expectedHealthState,
            Func<string, bool> descriptionCheck,
            Func<HealthReport, bool> extraChecks,
            string because)
        {
            var expectedHealthReportTimeToLive = this._scenarioOptions.DiscoveryPeriod.Multiply(3);
            this._healthReports.Should().Contain(
                report =>
                    report.HealthInformation.SourceId == Discoverer.HealthReportSourceId &&
                    report.HealthInformation.Property == Discoverer.HealthReportProperty &&
                    report.HealthInformation.TimeToLive == expectedHealthReportTimeToLive &&
                    report.HealthInformation.HealthState == expectedHealthState &&
                    report.HealthInformation.RemoveWhenExpired == true &&
                    (extraChecks == null || extraChecks(report)) &&
                    (descriptionCheck == null || descriptionCheck(report.HealthInformation.Description)),
                because: because);
        }

        // Mocking helpers
        private ApplicationWrapper CreateApp_1Service_SingletonPartition_1Replica(
            string appTypeName,
            string serviceTypeName,
            out ServiceWrapper service,
            out ReplicaWrapper replica,
            out PartitionWrapper partition,
            ServiceKind serviceKind = ServiceKind.Stateless)
        {
            service = this.CreateService(appTypeName, serviceTypeName, 1, 1, out var replicas, out var partitions, serviceKind);
            replica = replicas[0];
            partition = partitions[0];
            this.Mock_ServicesResponse(new Uri($"fabric:/{appTypeName}"), service);
            return SFTestHelpers.FakeApp(appTypeName, appTypeName);
        }
        private ApplicationWrapper CreateApp_1StatelessService_2Partition_2ReplicasEach(
            string appTypeName,
            string serviceTypeName,
            out ServiceWrapper service,
            out List<ReplicaWrapper> replicas,
            out List<PartitionWrapper> partitions)
        {
            service = this.CreateService(appTypeName, serviceTypeName, 2, 2, out replicas, out partitions);
            this.Mock_ServicesResponse(new Uri($"fabric:/{appTypeName}"), service);
            return SFTestHelpers.FakeApp(appTypeName, appTypeName);
        }
        private ApplicationWrapper CreateApp_2StatelessService_SingletonPartition_1Replica(
            string appTypeName,
            string serviceTypeName1,
            string serviceTypeName2,
            out ServiceWrapper service1,
            out ServiceWrapper service2,
            out ReplicaWrapper service1replica,
            out ReplicaWrapper service2replica,
            out PartitionWrapper service1partition,
            out PartitionWrapper service2partition)
        {
            service1 = this.CreateService(appTypeName, serviceTypeName1, 1, 1, out var replicas1, out var partitions1);
            service2 = this.CreateService(appTypeName, serviceTypeName2, 1, 1, out var replicas2, out var partitions2);
            service1replica = replicas1[0];
            service2replica = replicas2[0];
            service1partition = partitions1[0];
            service2partition = partitions2[0];
            this.Mock_ServicesResponse(new Uri($"fabric:/{appTypeName}"), service1, service2);
            return SFTestHelpers.FakeApp(appTypeName, appTypeName);
        }
        private ServiceWrapper CreateService(string appName, string serviceName, int numPartitions, int numReplicasPerPartition, out List<ReplicaWrapper> replicas, out List<PartitionWrapper> partitions, ServiceKind serviceKind = ServiceKind.Stateless)
        {
            var svcName = new Uri($"fabric:/{appName}/{serviceName}");
            var service = SFTestHelpers.FakeService(svcName, $"{appName}_{serviceName}_Type", serviceKind: serviceKind);
            replicas = new List<ReplicaWrapper>();
            partitions = new List<PartitionWrapper>();

            for (var i = 0; i < numPartitions; i++)
            {
                var partitionReplicas = Enumerable.Range(i * numReplicasPerPartition, numReplicasPerPartition).Select(replicaId => SFTestHelpers.FakeReplica(svcName, replicaId)).ToList();
                replicas.AddRange(partitionReplicas);
                var partition = SFTestHelpers.FakePartition();
                partitions.Add(partition);
                this.Mock_ReplicasResponse(partition.PartitionId, partitionReplicas.ToArray());
            }
            this.Mock_PartitionsResponse(svcName, partitions.ToArray());
            return service;
        }
        private void Mock_AppsResponse(params ApplicationWrapper[] apps)
        {
            /*
            this.Mock<ICachedServiceFabricCaller>()
                .Setup(m => m.GetApplicationListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(apps.ToList());
            */
            this.Mock<IQueryClientWrapper>()
                .Setup(m => m.GetApplicationListAsync(eachApiCallTimeout: TimeSpan.FromSeconds(60), cancellationToken: It.IsAny<CancellationToken>()))
                .ReturnsAsync(apps.ToList());
        }
        private void Mock_ServicesResponse(Uri applicationName, params ServiceWrapper[] services)
        {
            this.Mock<ICachedServiceFabricCaller>()
                .Setup(m => m.GetServiceListAsync(applicationName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(services.ToList());
        }
        private void Mock_PartitionsResponse(Uri serviceName, params PartitionWrapper[] partitions)
        {
            this.Mock<ICachedServiceFabricCaller>()
                .Setup(m => m.GetPartitionListAsync(serviceName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(partitions.ToList());
        }
        private void Mock_ReplicasResponse(Guid partitionId, params ReplicaWrapper[] replicas)
        {
            this.Mock<ICachedServiceFabricCaller>()
                .Setup(m => m.GetReplicaListAsync(partitionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(replicas.ToList());
        }
        private void Mock_ServiceLabels(ApplicationWrapper application, ServiceWrapper service, Dictionary<string, string> labels)
        {
            this.Mock<IServiceExtensionLabelsProvider>()
                .Setup(m => m.GetExtensionLabelsAsync(application, service, It.IsAny<CancellationToken>()))
                .ReturnsAsync(labels);
        }
        private void Mock_ServiceLabelsException(ApplicationWrapper application, ServiceWrapper service, Exception ex)
        {
            this.Mock<IServiceExtensionLabelsProvider>()
                .Setup(m => m.GetExtensionLabelsAsync(application, service, It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);
        }

        private string MockReplicaAdressWithListenerName(string appName, string serviceName, string[] listenerNameList)
        {
            var serviceNameUri = new Uri($"fabric:/{appName}/{serviceName}");
            var address = $"https://127.0.0.1/{serviceNameUri.Authority}/0";

            var endpoints = new Dictionary<string, string>();
            foreach (var lisernerName in listenerNameList)
            {
                endpoints.Add(lisernerName, address);
            }

            var replicaAddress = JsonSerializer.Serialize(
                new
                {
                    Endpoints = endpoints,
                });

            return replicaAddress;
        }
    }
}