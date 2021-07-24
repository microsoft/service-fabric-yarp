// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.Common.Telemetry;
using IslandGateway.FabricDiscovery.Util;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Tests.Common;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace IslandGateway.FabricDiscovery.IslandGatewayConfig.Tests
{
    public class IslandGatewayConfigProducerWorkerTests : TestAutoMockBase
    {
        private readonly Mock<ISnapshotProvider<IReadOnlyList<IslandGatewayBackendService>>> topologyProvider;
        private readonly Mock<IIslandGatewayConfigProducer> configProducer;

        public IslandGatewayConfigProducerWorkerTests()
        {
            this.Provide<IOperationLogger, NullOperationLogger>();
            this.topologyProvider = this.Mock<ISnapshotProvider<IReadOnlyList<IslandGatewayBackendService>>>();
            this.configProducer = this.Mock<IIslandGatewayConfigProducer>();
            this.Provide(Options.Create(new FabricDiscoveryOptions()));
        }

        [Fact]
        public void Constructor_Works()
        {
            this.Create<IslandGatewayConfigProducerWorker>();
        }

        [Fact]
        public void GetSnapshot_StartsNull()
        {
            // Arrange
            var sut = this.Create<IslandGatewayConfigProducerWorker>();

            // Act
            var actual = sut.GetConfig();

            // Assert
            actual.Should().BeNull();
        }

        [Fact]
        public async Task GetSnapshot_Works_EmptyTopology()
        {
            // Arrange
            var fabricTopology = new Snapshot<IReadOnlyList<IslandGatewayBackendService>>(new List<IslandGatewayBackendService>(), NullChangeToken.Singleton);
            this.topologyProvider
                .Setup(t => t.GetSnapshot())
                .Returns(fabricTopology);
            this.configProducer
                .Setup(c => c.ProduceConfig(fabricTopology.Value))
                .Returns((new List<ClusterConfig>(), new List<RouteConfig>()));
            var sut = this.Create<IslandGatewayConfigProducerWorker>();

            // Act & Assert
            await sut.StartAsync(CancellationToken.None);
            try
            {
                var snapshot = sut.GetConfig();

                snapshot.Should().NotBeNull();
                snapshot.Clusters.Should().BeEmpty();
                snapshot.Routes.Should().BeEmpty();
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task GetSnapshot_Works_OneBackendService()
        {
            // Arrange
            var fabricTopology = new Snapshot<IReadOnlyList<IslandGatewayBackendService>>(new List<IslandGatewayBackendService>(), NullChangeToken.Singleton);
            this.topologyProvider
                .Setup(t => t.GetSnapshot())
                .Returns(fabricTopology);
            var cluster1 = new ClusterConfig();
            var route1 = new RouteConfig();
            this.configProducer
                .Setup(c => c.ProduceConfig(fabricTopology.Value))
                .Returns((
                    Clusters: new List<ClusterConfig>()
                    {
                        cluster1,
                    },
                    Routes: new List<RouteConfig>()
                    {
                        route1,
                    }));
            var sut = this.Create<IslandGatewayConfigProducerWorker>();

            // Act & Assert
            await sut.StartAsync(CancellationToken.None);
            try
            {
                var snapshot = sut.GetConfig();

                snapshot.Should().NotBeNull();
                snapshot.Clusters.Should().Equal(cluster1);
                snapshot.Routes.Should().Equal(route1);
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task GetSnapshot_Works_ReactsToTopologyChanges()
        {
            // Arrange
            var ctss = new List<CancellationTokenSource>();
            this.topologyProvider
                .Setup(t => t.GetSnapshot())
                .Returns(() =>
                {
                    var cts = new CancellationTokenSource();
                    ctss.Add(cts);
                    return new Snapshot<IReadOnlyList<IslandGatewayBackendService>>(new List<IslandGatewayBackendService>(), new CancellationChangeToken(cts.Token));
                });
            this.configProducer
                .Setup(c => c.ProduceConfig(It.IsAny<IReadOnlyList<IslandGatewayBackendService>>()))
                .Returns((new List<ClusterConfig>(), new List<RouteConfig>()));
            var sut = this.Create<IslandGatewayConfigProducerWorker>();

            // Act & Assert
            await sut.StartAsync(CancellationToken.None);
            try
            {
                var snapshot0 = sut.GetConfig();

                var tcs = new TaskCompletionSource<int>();
                snapshot0.ChangeToken.RegisterChangeCallback(_ => tcs.SetResult(0), null);
                snapshot0.ChangeToken.HasChanged.Should().BeFalse();

                ctss.Should().HaveCount(1);
                ctss[0].Cancel();
                await tcs.Task;

                var snapshot1 = sut.GetConfig();
                snapshot1.Should().NotBeSameAs(snapshot0);
            }
            finally
            {
                await sut.StopAsync(CancellationToken.None);
            }
        }
    }
}