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
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology.Tests
{
    public class DirtyServicesTrackerTests
    {
        [Fact]
        public void Constructor_Works()
        {
            new DirtyServicesTracker();
        }

        [Fact]
        public void Basics_Work()
        {
            // Arrange
            var svc1 = new ServiceNameKey(new Uri("fabric:/MyApp/MySvc1"));
            var svc2 = new ServiceNameKey(new Uri("fabric:/MyApp/MySvc2"));
            var svc3 = new ServiceNameKey(new Uri("fabric:/MyApp/MySvc3"));

            var sut = new DirtyServicesTracker();

            // Act
            var actual0 = sut.GetSnapshot(); // []

            sut.Mark(svc1);
            var actual1 = sut.GetSnapshot(); // [svc1]

            sut.Mark(svc2);
            var actual2 = sut.GetSnapshot(); // [svc1, svc2]

            var rollbacker1 = sut.Unmark(new List<ServiceNameKey> { svc1 });
            var actual3 = sut.GetSnapshot(); // [svc2]

            sut.Mark(svc3);
            var actual4 = sut.GetSnapshot(); // [svc2, svc3]

            rollbacker1.Rollback();
            var actual5 = sut.GetSnapshot(); // [svc1, svc2, svc3]

            var rollbacker2 = sut.UnmarkAll();
            var actual6 = sut.GetSnapshot(); // []

            sut.Mark(svc1);
            var actual7 = sut.GetSnapshot(); // [svc1]

            rollbacker2.Rollback();
            var actual8 = sut.GetSnapshot(); // [svc1, svc2, svc3]

            // Assert
            actual0.Should().BeEmpty();
            actual1.Should().Equal(svc1);
            actual2.Should().BeEquivalentTo(svc1, svc2);
            actual3.Should().Equal(svc2);
            actual4.Should().BeEquivalentTo(svc2, svc3);
            actual5.Should().BeEquivalentTo(svc1, svc2, svc3);
            actual6.Should().BeEmpty();
            actual7.Should().Equal(svc1);
            actual8.Should().BeEquivalentTo(svc1, svc2, svc3);
        }
    }
}