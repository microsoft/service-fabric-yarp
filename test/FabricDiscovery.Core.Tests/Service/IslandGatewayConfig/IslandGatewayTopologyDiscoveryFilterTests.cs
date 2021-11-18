// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Yarp.ServiceFabric.FabricDiscovery.Topology;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.IslandGatewayConfig.Tests
{
    public class IslandGatewayTopologyDiscoveryFilterTests
    {
        private readonly IslandGatewayTopologyDiscoveryFilter sut = new IslandGatewayTopologyDiscoveryFilter();

        [Fact]
        public void ShouldDiscoverServicesOfServiceType_WithYarpExtension_ReturnsTrue()
        {
            this.sut
                .ShouldDiscoverServicesOfServiceType(
                    new DiscoveredApp(new ApplicationWrapper()),
                    new DiscoveredServiceType(
                        new ServiceTypeWrapper
                        {
                            Extensions = new Dictionary<string, string>
                            {
                                { "SomeOtherExtension", "anything" },
                                { "Yarp", "<Labels></Labels>" },
                            },
                        }))
                .Should().BeTrue();
        }

        [Fact]
        public void ShouldDiscoverServicesOfServiceType_NoYarpExtension_ReturnsFalse()
        {
            this.sut
                .ShouldDiscoverServicesOfServiceType(
                    new DiscoveredApp(new ApplicationWrapper()),
                    new DiscoveredServiceType(
                        new ServiceTypeWrapper
                        {
                            Extensions = new Dictionary<string, string>
                            {
                                { "SomeOtherExtension", "anything" },
                            },
                        }))
                .Should().BeFalse();
        }

        [Fact]
        public void ShouldDiscoverServicesOfServiceType_NullExtensions_ReturnsFalse()
        {
            this.sut
                .ShouldDiscoverServicesOfServiceType(
                    new DiscoveredApp(new ApplicationWrapper()),
                    new DiscoveredServiceType(new ServiceTypeWrapper()))
                .Should().BeFalse();
        }

        [Fact]
        public void ShouldDiscoverAppType_ReturnsTrue()
        {
            this.sut
                .ShouldDiscoverAppType(new ApplicationTypeWrapper())
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