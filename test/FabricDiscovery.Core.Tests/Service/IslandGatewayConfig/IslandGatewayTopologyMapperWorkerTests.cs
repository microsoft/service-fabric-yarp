// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Health;
using System.Fabric.Query;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.Common.Telemetry;
using IslandGateway.FabricDiscovery.Topology;
using IslandGateway.FabricDiscovery.Util;
using IslandGateway.ServiceFabricIntegration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Tests.Common;
using Xunit;

namespace IslandGateway.FabricDiscovery.IslandGatewayConfig.Tests
{
    public class IslandGatewayTopologyMapperWorkerTests : TestAutoMockBase
    {
        private readonly Mock<ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>> fabricTopologyProvider;

        public IslandGatewayTopologyMapperWorkerTests()
        {
            this.Provide<IOperationLogger, NullOperationLogger>();
            this.fabricTopologyProvider = this.Mock<ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>>();
            this.Provide(Options.Create(new FabricDiscoveryOptions()));
            this.Provide<IExtensionLabelsParser, ExtensionLabelsParser>();
        }

        [Fact]
        public void Constructor_Works()
        {
            this.Create<IslandGatewayConfigProducerWorker>();
        }

        [Fact]
        public async Task EmptyTopology_Works()
        {
            // Arrange
            this.fabricTopologyProvider
                .Setup(s => s.GetSnapshot())
                .Returns(new Snapshot<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>(new Dictionary<ApplicationNameKey, DiscoveredApp>(), NullChangeToken.Singleton));
            var sut = this.Create<IslandGatewayTopologyMapperWorker>();
            using var cts = new CancellationTokenSource();

            // Act
            await sut.StartAsync(cts.Token);
            var actual = sut.GetSnapshot();

            // Assert
            actual.Value.Should().BeEmpty();
        }

        [Fact]
        public async Task EchoServiceTopology_Works()
        {
            // Arrange
            this.SetupEchoServiceScenario();
            var sut = this.Create<IslandGatewayTopologyMapperWorker>();
            using var cts = new CancellationTokenSource();

            // Act
            await sut.StartAsync(cts.Token);
            var actual = sut.GetSnapshot();

            cts.Cancel();
            await sut.StopAsync(CancellationToken.None);

            // Assert
            actual.Value.Should().HaveCount(1);
            var echoService = actual.Value[0];
            echoService.FabricApplication.Application.ApplicationName.Should().Be(new ApplicationNameKey(new Uri("fabric:/CoreServices.IslandGateway")));
            echoService.FabricService.Service.ServiceName.Should().Be(new ServiceNameKey(new Uri("fabric:/CoreServices.IslandGateway/EchoService")));
            echoService.EffectiveLabels.Should().Equal(
                new Dictionary<string, string>
                {
                    { "IslandGateway.Enable", "true" },
                    { "IslandGateway.EnableDynamicOverrides", "false" },
                    { "IslandGateway.Routes.route1.Rule", "Host('echo.example.com') && Path('/echoservice/{**catchall}')" },
                    { "IslandGateway.Routes.route1.Transformations", "StripPrefix('/echoservice/')" },
                    { "IslandGateway.Routes.route2.Rule", "Host('tm-echo.example.com') && Path('/{**catchall}')" },
                    { "IslandGateway.Routes.route3.Rule", "Host('gw.example.com') && Path('/externalproxy/{**catchall}')" },
                    { "IslandGateway.Routes.route3.Metadata.RouteAction", "externalproxy" },
                    { "IslandGateway.Routes.route3.Transformations", "StripPrefix('/externalproxy/')" },
                    { "IslandGateway.Routes.TM.Rule", "Host('gw.example.com') && Path('/{**catchall}')" },
                    { "IslandGateway.Backend.Healthcheck.Enabled", "true" },
                    { "IslandGateway.Backend.Healthcheck.Path", "cf000000-7ae3-459b-927c-338515c95158/healthcheck" },
                    { "IslandGateway.Backend.Healthcheck.Timeout", "PT30S" },
                    { "IslandGateway.Backend.Healthcheck.Interval", "PT10S" },
                });
            echoService.FinalEffectiveLabels.Should().BeSameAs(echoService.EffectiveLabels);
        }

        private void SetupEchoServiceScenario()
        {
            var igwApp = this.SimulateIslandGatewayApp();

            var discoveredApps = new Dictionary<ApplicationNameKey, DiscoveredApp>
            {
                { igwApp.Application.ApplicationName, igwApp },
            };
            this.fabricTopologyProvider
                .Setup(s => s.GetSnapshot())
                .Returns(new Snapshot<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>(discoveredApps, NullChangeToken.Singleton));
        }

        private DiscoveredAppEx SimulateIslandGatewayApp()
        {
            var igwAppType = new ApplicationTypeWrapper
            {
                ApplicationTypeName = new ApplicationTypeNameKey("CoreServices.IslandGatewayAppType"),
                ApplicationTypeVersion = new ApplicationTypeVersionKey("1.16.01516.107-master-abc2ab1e"),
                DefaultParameters = new Dictionary<string, string>
                {
                    { "EchoService_DynamicOverrides", "false" },
                    { "EchoService_Route1_Rule", string.Empty },
                    { "EchoService_Route2_Rule", string.Empty },
                    { "EchoService_Route3_Rule", string.Empty },
                    { "EchoService_Route3_RouteAction", string.Empty },
                    { "EchoService_TrafficManager_Rule", string.Empty },
                },
                Status = ApplicationTypeStatus.Available,
            };
            var echoServiceType = new ServiceTypeWrapper
            {
                ServiceTypeName = new ServiceTypeNameKey("IslandGatewayEchoType"),
                ServiceTypeKind = ServiceDescriptionKind.Stateless,
                ServiceManifestName = "EchoServicePkg",
                ServiceManifestVersion = "1.16.01516.107-master-abc2ab1e",
                Extensions = new Dictionary<string, string>
                {
                    {
                        "IslandGateway",
                        @"<Labels xmlns=""http://schemas.microsoft.com/2015/03/fabact-no-schema"">
                              <Label Key=""IslandGateway.Enable"">true</Label>
                              <Label Key=""IslandGateway.EnableDynamicOverrides"">[EchoService_DynamicOverrides]</Label>
                              <Label Key=""IslandGateway.Routes.route1.Rule"">[EchoService_Route1_Rule]</Label>
                              <Label Key=""IslandGateway.Routes.route1.Transformations"">StripPrefix('/echoservice/')</Label>
                              <Label Key=""IslandGateway.Routes.route2.Rule"">[EchoService_Route2_Rule]</Label>
                              <Label Key=""IslandGateway.Routes.route3.Rule"">[EchoService_Route3_Rule]</Label>
                              <Label Key=""IslandGateway.Routes.route3.Metadata.RouteAction"">[EchoService_Route3_RouteAction]</Label>
                              <Label Key=""IslandGateway.Routes.route3.Transformations"">StripPrefix('/externalproxy/')</Label>
                              <Label Key=""IslandGateway.Routes.TM.Rule"">[EchoService_TrafficManager_Rule]</Label>
                              <!-- enable health probe -->
                              <Label Key=""IslandGateway.Backend.Healthcheck.Enabled"">true</Label>
                              <!-- use CoreServiceShim middleware health endpoint to provide echo service gracefully shutdown -->
                              <Label Key=""IslandGateway.Backend.Healthcheck.Path"">cf000000-7ae3-459b-927c-338515c95158/healthcheck</Label>
                              <Label Key=""IslandGateway.Backend.Healthcheck.Timeout"">PT30S</Label>
                              <Label Key=""IslandGateway.Backend.Healthcheck.Interval"">PT10S</Label>
                          </Labels>"
                    },
                },
            };
            var igwApp = new ApplicationWrapper
            {
                ApplicationName = new ApplicationNameKey(new Uri("fabric:/CoreServices.IslandGateway")),
                ApplicationTypeName = igwAppType.ApplicationTypeName,
                ApplicationTypeVersion = igwAppType.ApplicationTypeVersion,
                ApplicationParameters = new Dictionary<string, string>
                {
                    { "EchoService_Route1_Rule", "Host('echo.example.com') && Path('/echoservice/{**catchall}')" },
                    { "EchoService_Route2_Rule", "Host('tm-echo.example.com') && Path('/{**catchall}')" },
                    { "EchoService_Route3_Rule", "Host('gw.example.com') && Path('/externalproxy/{**catchall}')" },
                    { "EchoService_Route3_RouteAction", "externalproxy" },
                    { "EchoService_TrafficManager_Rule", "Host('gw.example.com') && Path('/{**catchall}')" },
                },
            };
            var echoService = new ServiceWrapper
            {
                ServiceName = new ServiceNameKey(new Uri("fabric:/CoreServices.IslandGateway/EchoService")),
                ServiceTypeName = echoServiceType.ServiceTypeName,
                ServiceManifestVersion = echoServiceType.ServiceManifestVersion,
                ServiceKind = ServiceKind.Stateless,
            };
            var echoPartition = new PartitionWrapper
            {
                PartitionId = Guid.Parse("3ba7dfee-9271-482a-8e82-e98949edb1cf"),
            };
            var echoReplicas = new[]
            {
                new ReplicaWrapper
                {
                    Id = 132587450787126998,
                    ReplicaAddress = @"{""Endpoints"":{""Service Endpoint Secure"":""https://127.0.0.6:28137/3ba7dfee-9271-482a-8e82-e98949edb1cf/132587450787126998""}}",
                    HealthState = HealthState.Ok,
                    ReplicaStatus = ServiceReplicaStatus.Ready,
                    Role = ReplicaRole.None,
                    ServiceKind = ServiceKind.Stateless,
                },
                new ReplicaWrapper
                {
                    Id = 132587449112860863,
                    ReplicaAddress = @"{""Endpoints"":{""Service Endpoint Secure"":""https://127.0.0.5:28874/3ba7dfee-9271-482a-8e82-e98949edb1cf/132587449112860863""}}",
                    HealthState = HealthState.Ok,
                    ReplicaStatus = ServiceReplicaStatus.Ready,
                    Role = ReplicaRole.None,
                    ServiceKind = ServiceKind.Stateless,
                },
            };

            var discoveredIgwAppType = new DiscoveredAppType(igwAppType);
            var discoveredEchoServiceType = new DiscoveredServiceType(echoServiceType);
            var discoveredIgwAppTypeEx = new DiscoveredAppTypeEx(
                discoveredIgwAppType,
                new Dictionary<ServiceTypeNameKey, DiscoveredServiceType>
                {
                    { echoServiceType.ServiceTypeName, discoveredEchoServiceType },
                });

            var discoveredEchoService = new DiscoveredService(discoveredEchoServiceType, echoService);
            var discoveredEchoReplicas = echoReplicas.Select(e => new DiscoveredReplica(e)).ToList();
            var discoveredEchoServiceEx = new DiscoveredServiceEx(
                discoveredEchoService,
                new[]
                {
                    new DiscoveredPartition(echoPartition, discoveredEchoReplicas),
                });

            var discoveredIgwApp = new DiscoveredApp(igwApp);
            var discoveredIgwAppEx = new DiscoveredAppEx(
                discoveredIgwApp,
                discoveredIgwAppTypeEx,
                new Dictionary<ServiceNameKey, DiscoveredService>
                {
                    { echoService.ServiceName, discoveredEchoServiceEx },
                });

            return discoveredIgwAppEx;
        }
    }
}