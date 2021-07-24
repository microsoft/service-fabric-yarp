// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using IslandGateway.FabricDiscovery.IslandGatewayConfig;
using IslandGateway.FabricDiscovery.Util;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Moq;
using Tests.Common;
using Xunit;

namespace IslandGateway.FabricDiscovery.Controllers.Tests
{
    public class IgwConfigControllerTests : TestAutoMockBase
    {
        private readonly DIAdapter diAdapter;

        public IgwConfigControllerTests()
        {
            this.diAdapter = new DIAdapter();
            this.Provide(this.diAdapter);
        }

        [Fact]
        public async Task GetYarpConfig_DIAdapterNotConfigured_Returns503()
        {
            // Arrange
            var controller = this.Create<YarpConfigController>();

            // Act
            var action = await controller.GetYarpConfig();

            // Assert
            Assert.IsType<StatusCodeResult>(action).StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        }

        [Fact]
        public async Task GetYarpConfig_NoConfigYet_Returns503()
        {
            // Arrange
            var configProviderMock = new Mock<ISnapshotProvider<IslandGatewaySerializedConfig>>();
            configProviderMock.Setup(c => c.GetSnapshot()).Returns((Snapshot<IslandGatewaySerializedConfig>)null);

            var services = new ServiceCollection();
            services.AddSingleton(configProviderMock.Object);

            this.diAdapter.SetServiceProvider(services.BuildServiceProvider());

            var controller = this.Create<YarpConfigController>();

            // Act
            var action = await controller.GetYarpConfig();

            // Assert
            Assert.IsType<StatusCodeResult>(action).StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        }

        [Fact]
        public async Task GetYarpConfig_WithConfig_Returns200()
        {
            // Arrange
            var configProviderMock = new Mock<ISnapshotProvider<IslandGatewaySerializedConfig>>();

            var snapshotValue = new IslandGatewaySerializedConfig(
                bytes: Encoding.UTF8.GetBytes(@"{""clusters"":[],""routes"":[]}"),
                etag: "\"etag0\"",
                contentType: "application/json");
            var snapshot = new Snapshot<IslandGatewaySerializedConfig>(snapshotValue, NullChangeToken.Singleton);
            configProviderMock.Setup(c => c.GetSnapshot()).Returns(snapshot);

            var services = new ServiceCollection();
            services.AddSingleton(configProviderMock.Object);

            this.diAdapter.SetServiceProvider(services.BuildServiceProvider());
            var controller = this.CreateController();

            // Act
            var action = await controller.GetYarpConfig();

            // Assert
            controller.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
            Assert.IsType<EmptyResult>(action);
        }

        private YarpConfigController CreateController()
        {
            var controller = this.Create<YarpConfigController>();
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            };
            return controller;
        }
    }
}