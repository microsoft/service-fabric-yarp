// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using Tests.Common;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.Util;

namespace Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig.Tests
{
    public class SFYarpConfigProducerWorkerTests : TestAutoMockBase
    {
        private readonly Mock<ISnapshotProvider<IReadOnlyList<SFYarpBackendService>>> topologyProvider;
        private readonly Mock<ISFYarpConfigProducer> configProducer;

        public SFYarpConfigProducerWorkerTests()
        {
            this.Provide<IOperationLogger, NullOperationLogger>();
            this.topologyProvider = this.Mock<ISnapshotProvider<IReadOnlyList<SFYarpBackendService>>>();
            this.configProducer = this.Mock<ISFYarpConfigProducer>();
            this.Provide(Options.Create(new FabricDiscoveryOptions()));
        }

        [Fact]
        public void Constructor_Works()
        {
            this.Create<SFYarpConfigProducerWorker>();
        }

        [Fact]
        public void GetSnapshot_StartsNull()
        {
            // Arrange
            var sut = this.Create<SFYarpConfigProducerWorker>();

            // Act
            var actual = sut.GetConfig();

            // Assert
            actual.Should().BeNull();
        }

        [Fact]
        public async Task GetSnapshot_Works_EmptyTopology()
        {
            // Arrange
            var fabricTopology = new Snapshot<IReadOnlyList<SFYarpBackendService>>(new List<SFYarpBackendService>(), NullChangeToken.Singleton);
            this.topologyProvider
                .Setup(t => t.GetSnapshot())
                .Returns(fabricTopology);
            this.configProducer
                .Setup(c => c.ProduceConfig(fabricTopology.Value))
                .Returns((new List<ClusterConfig>(), new List<RouteConfig>()));
            var sut = this.Create<SFYarpConfigProducerWorker>();

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
            var fabricTopology = new Snapshot<IReadOnlyList<SFYarpBackendService>>(new List<SFYarpBackendService>(), NullChangeToken.Singleton);
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
            var sut = this.Create<SFYarpConfigProducerWorker>();

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
                    return new Snapshot<IReadOnlyList<SFYarpBackendService>>(new List<SFYarpBackendService>(), new CancellationChangeToken(cts.Token));
                });
            this.configProducer
                .Setup(c => c.ProduceConfig(It.IsAny<IReadOnlyList<SFYarpBackendService>>()))
                .Returns((new List<ClusterConfig>(), new List<RouteConfig>()));
            var sut = this.Create<SFYarpConfigProducerWorker>();

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