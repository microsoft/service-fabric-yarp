// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology.Tests
{
    public class TopologyDiscoveryFilterTests
    {
        private readonly TopologyDiscoveryFilter sut = new TopologyDiscoveryFilter();

        [Fact]
        public void ShouldDiscoverAppType_ReturnsTrue()
        {
            this.sut
                .ShouldDiscoverAppType(new ApplicationTypeWrapper())
                .Should().BeTrue();
        }

        [Fact]
        public void ShouldDiscoverServicesOfServiceType_ReturnsTrue()
        {
            this.sut
                .ShouldDiscoverServicesOfServiceType(
                    new DiscoveredApp(new ApplicationWrapper()),
                    new DiscoveredServiceType(new ServiceTypeWrapper()))
                .Should().BeTrue();
        }

        [Fact]
        public void ShouldDiscoverApp_ReturnsTrue()
        {
            this.sut
                .ShouldDiscoverApp(
                    new DiscoveredAppTypeEx(
                        new DiscoveredAppType(new ApplicationTypeWrapper()),
                        new Dictionary<ServiceTypeNameKey, DiscoveredServiceType>()),
                    new DiscoveredApp(new ApplicationWrapper()))
                .Should().BeTrue();
        }

        [Fact]
        public void ShouldDiscoverService_ReturnsTrue()
        {
            this.sut
                .ShouldDiscoverService(
                    new DiscoveredApp(new ApplicationWrapper()),
                    new DiscoveredService(
                        new DiscoveredServiceType(new ServiceTypeWrapper()),
                        new ServiceWrapper()))
                .Should().BeTrue();
        }
    }
}