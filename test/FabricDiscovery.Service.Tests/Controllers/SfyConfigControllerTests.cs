﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Tests.Common;
using Xunit;
using Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig;
using Yarp.ServiceFabric.FabricDiscovery.Util;

namespace Yarp.ServiceFabric.FabricDiscovery.Controllers.Tests
{
    public class SfyConfigControllerTests : TestAutoMockBase
    {
        [Fact]
        public async Task GetYarpConfig_NoConfigYet_Returns503()
        {
            // Arrange
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
            var snapshotValue = new SFYarpSerializedConfig(
                bytes: Encoding.UTF8.GetBytes(@"{""clusters"":[],""routes"":[]}"),
                etag: "\"etag0\"",
                contentType: "application/json");
            var snapshot = new Snapshot<SFYarpSerializedConfig>(snapshotValue, NullChangeToken.Singleton);
            this.Mock<ISnapshotProvider<SFYarpSerializedConfig>>().Setup(c => c.GetSnapshot()).Returns(snapshot);

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