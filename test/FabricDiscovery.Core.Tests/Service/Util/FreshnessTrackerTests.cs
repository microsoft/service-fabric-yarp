// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Yarp.ServiceFabric.FabricDiscovery.Util.Tests
{
    public class FreshnessTrackerTests
    {
        [Fact]
        public void Constructor_Works()
        {
            new FreshnessTracker();
        }

        [Fact]
        public void Freshness_StartsAtZero()
        {
            // Arrange
            var sut = new FreshnessTracker();

            // Act & Assert
            sut.Freshness.Should().Be(TimeSpan.Zero);
        }

        [Fact]
        public void Freshness_GrowsAfterSetFresh()
        {
            // This is an *absurd* timeout to minimize likelihood of test flakiness. This test should really take microseconds, but Windows isn't a real-time OS, so play it safe....
            var timeout = TimeSpan.FromMinutes(1);

            // Arrange
            var sut = new FreshnessTracker();

            // Act & Assert
            sut.SetFresh();

            long v1 = sut.Freshness.Ticks;
            long v2 = v1;

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout && v1 == v2)
            {
                v2 = sut.Freshness.Ticks;
            }

            v2.Should().BeGreaterThan(v1);
        }

        [Fact]
        public void Reset_Works()
        {
            // This is an *absurd* timeout to minimize likelihood of test flakiness. This test should really take microseconds, but Windows isn't a real-time OS, so play it safe....
            var timeout = TimeSpan.FromMinutes(1);

            // Arrange
            var sut = new FreshnessTracker();

            // Act & Assert
            sut.SetFresh();

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout && sut.Freshness.Ticks == 0)
            {
            }

            sut.Freshness.Should().NotBe(TimeSpan.Zero);

            sut.Reset();
            sut.Freshness.Should().Be(TimeSpan.Zero);
        }
    }
}
