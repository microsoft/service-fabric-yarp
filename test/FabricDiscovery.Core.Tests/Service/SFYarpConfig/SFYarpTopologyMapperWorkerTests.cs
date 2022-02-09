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
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Tests.Common;
using Xunit;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.Topology;
using Yarp.ServiceFabric.FabricDiscovery.Util;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig.Tests
{
    public class SFYarpTopologyMapperWorkerTests : TestAutoMockBase
    {
        private readonly Mock<ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>> fabricTopologyProvider;

        public SFYarpTopologyMapperWorkerTests()
        {
            this.Provide<IOperationLogger, NullOperationLogger>();
            this.fabricTopologyProvider = this.Mock<ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>>();
            this.Provide(Options.Create(new FabricDiscoveryOptions()));
            this.Provide<IExtensionLabelsParser, ExtensionLabelsParser>();
        }

        [Fact]
        public void Constructor_Works()
        {
            this.Create<SFYarpConfigProducerWorker>();
        }

        [Fact]
        public async Task EmptyTopology_Works()
        {
            // Arrange
            this.fabricTopologyProvider
                .Setup(s => s.GetSnapshot())
                .Returns(new Snapshot<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>(new Dictionary<ApplicationNameKey, DiscoveredApp>(), NullChangeToken.Singleton));
            var sut = this.Create<SFYarpTopologyMapperWorker>();
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
            var sut = this.Create<SFYarpTopologyMapperWorker>();
            using var cts = new CancellationTokenSource();

            // Act
            await sut.StartAsync(cts.Token);
            var actual = sut.GetSnapshot();

            cts.Cancel();
            await sut.StopAsync(CancellationToken.None);

            // Assert
            actual.Value.Should().HaveCount(1);
            var echoService = actual.Value[0];
            echoService.FabricApplication.Application.ApplicationName.Should().Be(new ApplicationNameKey(new Uri("fabric:/CoreServices.SFYarp")));
            echoService.FabricService.Service.ServiceName.Should().Be(new ServiceNameKey(new Uri("fabric:/CoreServices.SFYarp/EchoService")));
            echoService.EffectiveLabels.Should().Equal(
                new Dictionary<string, string>
                {
                    { "Yarp.Enable", "true" },
                    { "Yarp.EnableDynamicOverrides", "false" },
                    { "Yarp.Routes.route1.Rule", "Host('echo.example.com') && Path('/echoservice/{**catchall}')" },
                    { "Yarp.Routes.route1.Transformations", "StripPrefix('/echoservice/')" },
                    { "Yarp.Routes.route2.Rule", "Host('tm-echo.example.com') && Path('/{**catchall}')" },
                    { "Yarp.Routes.route3.Rule", "Host('gw.example.com') && Path('/externalproxy/{**catchall}')" },
                    { "Yarp.Routes.route3.Metadata.RouteAction", "externalproxy" },
                    { "Yarp.Routes.route3.Transformations", "StripPrefix('/externalproxy/')" },
                    { "Yarp.Routes.TM.Rule", "Host('gw.example.com') && Path('/{**catchall}')" },
                    { "Yarp.Backend.Healthcheck.Enabled", "true" },
                    { "Yarp.Backend.Healthcheck.Path", "cf000000-7ae3-459b-927c-338515c95158/healthcheck" },
                    { "Yarp.Backend.Healthcheck.Timeout", "PT30S" },
                    { "Yarp.Backend.Healthcheck.Interval", "PT10S" },
                });
            echoService.FinalEffectiveLabels.Should().BeSameAs(echoService.EffectiveLabels);
        }

        private void SetupEchoServiceScenario()
        {
            var sfyApp = this.SimulateSFYarpApp();

            var discoveredApps = new Dictionary<ApplicationNameKey, DiscoveredApp>
            {
                { sfyApp.Application.ApplicationName, sfyApp },
            };
            this.fabricTopologyProvider
                .Setup(s => s.GetSnapshot())
                .Returns(new Snapshot<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>(discoveredApps, NullChangeToken.Singleton));
        }

        private DiscoveredAppEx SimulateSFYarpApp()
        {
            var sfyAppType = new ApplicationTypeWrapper
            {
                ApplicationTypeName = new ApplicationTypeNameKey("CoreServices.SFYarpAppType"),
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
                ServiceTypeName = new ServiceTypeNameKey("SFYarpEchoType"),
                ServiceTypeKind = ServiceDescriptionKind.Stateless,
                ServiceManifestName = "EchoServicePkg",
                ServiceManifestVersion = "1.16.01516.107-master-abc2ab1e",
                Extensions = new Dictionary<string, string>
                {
                    {
                        "Yarp",
                        @"<Labels xmlns=""http://schemas.microsoft.com/2015/03/fabact-no-schema"">
                              <Label Key=""Yarp.Enable"">true</Label>
                              <Label Key=""Yarp.EnableDynamicOverrides"">[EchoService_DynamicOverrides]</Label>
                              <Label Key=""Yarp.Routes.route1.Rule"">[EchoService_Route1_Rule]</Label>
                              <Label Key=""Yarp.Routes.route1.Transformations"">StripPrefix('/echoservice/')</Label>
                              <Label Key=""Yarp.Routes.route2.Rule"">[EchoService_Route2_Rule]</Label>
                              <Label Key=""Yarp.Routes.route3.Rule"">[EchoService_Route3_Rule]</Label>
                              <Label Key=""Yarp.Routes.route3.Metadata.RouteAction"">[EchoService_Route3_RouteAction]</Label>
                              <Label Key=""Yarp.Routes.route3.Transformations"">StripPrefix('/externalproxy/')</Label>
                              <Label Key=""Yarp.Routes.TM.Rule"">[EchoService_TrafficManager_Rule]</Label>
                              <!-- enable health probe -->
                              <Label Key=""Yarp.Backend.Healthcheck.Enabled"">true</Label>
                              <!-- use CoreServiceShim middleware health endpoint to provide echo service gracefully shutdown -->
                              <Label Key=""Yarp.Backend.Healthcheck.Path"">cf000000-7ae3-459b-927c-338515c95158/healthcheck</Label>
                              <Label Key=""Yarp.Backend.Healthcheck.Timeout"">PT30S</Label>
                              <Label Key=""Yarp.Backend.Healthcheck.Interval"">PT10S</Label>
                          </Labels>"
                    },
                },
            };
            var sfyApp = new ApplicationWrapper
            {
                ApplicationName = new ApplicationNameKey(new Uri("fabric:/CoreServices.SFYarp")),
                ApplicationTypeName = sfyAppType.ApplicationTypeName,
                ApplicationTypeVersion = sfyAppType.ApplicationTypeVersion,
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
                ServiceName = new ServiceNameKey(new Uri("fabric:/CoreServices.SFYarp/EchoService")),
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

            var discoveredSfyAppType = new DiscoveredAppType(sfyAppType);
            var discoveredEchoServiceType = new DiscoveredServiceType(echoServiceType);
            var discoveredSfyAppTypeEx = new DiscoveredAppTypeEx(
                discoveredSfyAppType,
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

            var discoveredSfyApp = new DiscoveredApp(sfyApp);
            var discoveredSfyAppEx = new DiscoveredAppEx(
                discoveredSfyApp,
                discoveredSfyAppTypeEx,
                new Dictionary<ServiceNameKey, DiscoveredService>
                {
                    { echoService.ServiceName, discoveredEchoServiceEx },
                });

            return discoveredSfyAppEx;
        }
    }
}