// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric.Description;
using System.Fabric.Query;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Tests.Common;
using Xunit;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.FabricWrapper;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology.Tests
{
    public class ServiceDiscoveryManagerTests : TestAutoMockBase
    {
        private readonly TestMetricCreator metricCreator;
        private readonly Mock<IAppTypeDiscoveryManager> appTypeDiscoveryManagerMock;
        private readonly Mock<IQueryClientWrapper> queryClientMock;
        private readonly Mock<IServiceManagementClientWrapper> serviceManagementClientMock;
        private readonly Mock<IServiceDiscoveryHelper> discoveryHelperMock;

        public ServiceDiscoveryManagerTests()
        {
            this.Provide<IOperationLogger, NullOperationLogger>();
            this.metricCreator = this.Provide<IMetricCreator, TestMetricCreator>();
            this.queryClientMock = this.Mock<IQueryClientWrapper>();
            this.serviceManagementClientMock = this.Mock<IServiceManagementClientWrapper>();
            this.appTypeDiscoveryManagerMock = this.Mock<IAppTypeDiscoveryManager>();
            this.discoveryHelperMock = this.Mock<IServiceDiscoveryHelper>();
        }

        [Fact]
        public void Constructor_Works()
        {
            this.Create<ServiceDiscoveryManager>();
        }

        [Fact]
        public async Task RefreshAll_Works()
        {
            // Arrange
            this.SetupScenario1();
            var sut = this.Create<ServiceDiscoveryManager>();

            // Act
            await sut.RefreshAll(CancellationToken.None);
            var discoveredApps = sut.DiscoveredApps;

            // Assert
            discoveredApps.Should().HaveCount(1);
            var discoveredApp = discoveredApps[new ApplicationNameKey(new Uri("fabric:/App1"))];
            var discoveredAppEx = discoveredApp.Should().BeOfType<DiscoveredAppEx>().Subject;
            discoveredAppEx.Services.Should().HaveCount(1);
            discoveredAppEx.Services[new ServiceNameKey(new Uri("fabric:/App1/Svc1"))].Should().BeOfType<DiscoveredService>();
        }

        [Fact]
        public async Task RefreshDirtyServices_NothingToRefresh_Works()
        {
            // Arrange
            this.SetupScenario1();
            var sut = this.Create<ServiceDiscoveryManager>();
            await sut.RefreshAll(CancellationToken.None);

            // Act
            await sut.RefreshDirtyServices(CancellationToken.None);
            var discoveredApps = sut.DiscoveredApps;

            // Assert
            discoveredApps.Should().HaveCount(1);
            var discoveredApp = discoveredApps[new ApplicationNameKey(new Uri("fabric:/App1"))];
            var discoveredAppEx = discoveredApp.Should().BeOfType<DiscoveredAppEx>().Subject;
            discoveredAppEx.Services.Should().HaveCount(1);
            discoveredAppEx.Services[new ServiceNameKey(new Uri("fabric:/App1/Svc1"))].Should().BeOfType<DiscoveredService>();
        }

        private void SetupScenario1()
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

            this.appTypeDiscoveryManagerMock
                .Setup(a => a.GetInterestingAppTypeNames())
                .Returns(new[] { appType1.ApplicationTypeName, });
            this.queryClientMock
                .Setup(q => q.GetApplicationsAsync(It.Is<ApplicationQueryDescription>(d => d.ApplicationTypeNameFilter == appType1.ApplicationTypeName), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns(new[] { app1 }.ToAsyncEnumerable());
            this.discoveryHelperMock
                .Setup(d => d.DiscoverApp(app1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new DiscoveredAppEx(
                        new DiscoveredApp(app1),
                        discoveredAppType1,
                        new Dictionary<ServiceNameKey, DiscoveredService>()
                        {
                            { service1.ServiceName, new DiscoveredService(discoveredServiceType1, service1) },
                        }));
        }
    }
}