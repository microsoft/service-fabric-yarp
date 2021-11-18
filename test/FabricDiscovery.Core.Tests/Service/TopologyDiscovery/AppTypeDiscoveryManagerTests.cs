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
    public class AppTypeDiscoveryManagerTests : TestAutoMockBase
    {
        private readonly Mock<IQueryClientWrapper> queryClientMock;
        private readonly Mock<TopologyDiscoveryFilter> filter;

        public AppTypeDiscoveryManagerTests()
        {
            this.Provide<IOperationLogger, NullOperationLogger>();
            this.queryClientMock = this.Mock<IQueryClientWrapper>();
            this.filter = this.Mock<TopologyDiscoveryFilter>();
        }

        [Fact]
        public void Constructor_Works()
        {
            this.Create<AppTypeDiscoveryManager>();
        }

        [Fact]
        public void GetInterestingAppTypeNames_StartsEmpty()
        {
            // Arrange
            var sut = this.Create<AppTypeDiscoveryManager>();

            // Act
            var actual = sut.GetInterestingAppTypeNames();

            // Assert
            actual.Should().BeEmpty();
        }

        [Fact]
        public async Task Refresh_DiscoversInterestingAppTypesOnly()
        {
            // Arrange
            var (appType1, appType2) = this.SetupAppTypes_Scenario1();
            var sut = this.Create<AppTypeDiscoveryManager>();

            // Act
            await sut.Refresh(CancellationToken.None);
            var actual = sut.GetInterestingAppTypeNames();

            // Assert
            actual.Should().Equal(appType2.ApplicationTypeName);
        }

        [Fact]
        public async Task GetAppTypeInfo_PreDiscoveredAppType_ReturnsCached()
        {
            // Arrange
            var (appType1, appType2) = this.SetupAppTypes_Scenario1();
            var sut = this.Create<AppTypeDiscoveryManager>();
            await sut.Refresh(CancellationToken.None);

            // Act
            var actual1 = await sut.GetAppTypeInfo(appType1.ApplicationTypeName, appType1.ApplicationTypeVersion, CancellationToken.None);
            var actual2 = await sut.GetAppTypeInfo(appType2.ApplicationTypeName, appType2.ApplicationTypeVersion, CancellationToken.None);

            // Assert
            actual1.Should().BeOfType<DiscoveredAppType>();
            actual1.AppType.Should().BeSameAs(appType1);

            actual2.Should().BeOfType<DiscoveredAppTypeEx>();
            actual2.AppType.Should().BeSameAs(appType2);

            this.queryClientMock.Verify(q => q.GetApplicationTypesAsync(It.IsAny<PagedApplicationTypeQueryDescription>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
            this.queryClientMock.Verify(q => q.GetServiceTypesAsync(It.IsAny<ApplicationTypeNameKey>(), It.IsAny<ApplicationTypeVersionKey>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAppTypeInfo_NotPreDiscoveredAppType_ReturnsFresh()
        {
            // Arrange
            var (appType1, appType2) = this.SetupAppTypes_Scenario1();
            var sut = this.Create<AppTypeDiscoveryManager>();

            // Act
            var actual1 = await sut.GetAppTypeInfo(appType1.ApplicationTypeName, appType1.ApplicationTypeVersion, CancellationToken.None);
            var actual2 = await sut.GetAppTypeInfo(appType2.ApplicationTypeName, appType2.ApplicationTypeVersion, CancellationToken.None);

            // Assert
            actual1.Should().BeOfType<DiscoveredAppType>();
            actual1.AppType.Should().BeSameAs(appType1);

            actual2.Should().BeOfType<DiscoveredAppTypeEx>();
            actual2.AppType.Should().BeSameAs(appType2);

            this.queryClientMock.Verify(q => q.GetApplicationTypesAsync(It.IsAny<PagedApplicationTypeQueryDescription>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            this.queryClientMock.Verify(q => q.GetServiceTypesAsync(It.IsAny<ApplicationTypeNameKey>(), It.IsAny<ApplicationTypeVersionKey>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Scenario 1: Two app types, but only appType2 is "interesting" (i.e. selected by the filter).
        /// </summary>
        private (ApplicationTypeWrapper AppType1, ApplicationTypeWrapper AppType2) SetupAppTypes_Scenario1()
        {
            var appType1 = new ApplicationTypeWrapper
            {
                ApplicationTypeName = new ApplicationTypeNameKey("AppTypeName1"),
                ApplicationTypeVersion = new ApplicationTypeVersionKey("AppType1Version1"),
                DefaultParameters = new Dictionary<string, string>(),
                Status = ApplicationTypeStatus.Available,
            };
            var appType2 = new ApplicationTypeWrapper
            {
                ApplicationTypeName = new ApplicationTypeNameKey("AppTypeName2"),
                ApplicationTypeVersion = new ApplicationTypeVersionKey("AppType2Version1"),
                DefaultParameters = new Dictionary<string, string>(),
                Status = ApplicationTypeStatus.Available,
            };

            this.queryClientMock
                .Setup(q => q.GetApplicationTypesAsync(
                    It.Is<PagedApplicationTypeQueryDescription>(d => d.ApplicationTypeDefinitionKindFilter == ApplicationTypeDefinitionKindFilter.Default && d.ApplicationTypeNameFilter == null && d.ApplicationTypeVersionFilter == null),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { appType1, appType2 }.ToAsyncEnumerable());
            this.queryClientMock
                .Setup(q => q.GetApplicationTypesAsync(
                    It.Is<PagedApplicationTypeQueryDescription>(d => d.ApplicationTypeDefinitionKindFilter == ApplicationTypeDefinitionKindFilter.Default && d.ApplicationTypeNameFilter == appType1.ApplicationTypeName && d.ApplicationTypeVersionFilter == appType1.ApplicationTypeVersion),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { appType1 }.ToAsyncEnumerable());
            this.queryClientMock
                .Setup(q => q.GetApplicationTypesAsync(
                    It.Is<PagedApplicationTypeQueryDescription>(d => d.ApplicationTypeDefinitionKindFilter == ApplicationTypeDefinitionKindFilter.Default && d.ApplicationTypeNameFilter == appType2.ApplicationTypeName && d.ApplicationTypeVersionFilter == appType2.ApplicationTypeVersion),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new[] { appType2 }.ToAsyncEnumerable());

            this.filter
                .Setup(f => f.ShouldDiscoverAppType(appType1))
                .Returns(false);
            this.filter
                .Setup(f => f.ShouldDiscoverAppType(appType2))
                .Returns(true); // Only appType2 is interesting in this example
            this.queryClientMock
                .Setup(q => q.GetServiceTypesAsync(appType2.ApplicationTypeName, appType2.ApplicationTypeVersion, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns(new ServiceTypeWrapper[0].ToAsyncEnumerable());

            return (appType1, appType2);
        }
    }
}