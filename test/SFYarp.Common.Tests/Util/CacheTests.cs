// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using Tests.Common;
using Xunit;
using Yarp.ServiceFabric.Common.Abstractions.Time;
using Yarp.ServiceFabric.Common.Util;

namespace Yarp.ServiceFabric.Common.Tests
{
    public class CacheTests : TestAutoMockBase
    {
        private VirtualMonotonicTimer timer;
        public CacheTests()
        {
            this.timer = new VirtualMonotonicTimer();
            this.Provide<IMonotonicTimer>(this.timer);
        }

        [Fact]
        public void Get_NotExpired_KeyIsPresent()
        {
            // Arrange
            TimeSpan expirationTimeOffset = TimeSpan.FromMinutes(12);
            string key = "some key";
            string value = "some awesome value";
            var cache = new Cache<string>(this.timer, expirationTimeOffset);
            cache.Set(key, value);

            // Act
            var firstPresent = cache.TryGetValue(key, out string firstValueGot);
            this.timer.AdvanceClockBy(expirationTimeOffset);
            var secondPresent = cache.TryGetValue(key, out string secondValueGot);

            // Assert
            firstValueGot.Should().Be(value);
            firstPresent.Should().BeTrue();
            secondValueGot.Should().Be(value);
            secondPresent.Should().BeTrue();
        }

        [Fact]
        public void Get_Expired_KeyIsNotPresent()
        {
            // Arrange
            TimeSpan expirationTimeOffset = TimeSpan.FromMinutes(12);
            string key = "some key";
            string value = "some awesome value";
            var cache = new Cache<string>(this.timer, expirationTimeOffset);
            cache.Set(key, value);

            // Act
            var firstPresent = cache.TryGetValue(key, out string firstValueGot);
            this.timer.AdvanceClockBy(expirationTimeOffset);
            this.timer.AdvanceClockBy(expirationTimeOffset);
            var secondPresent = cache.TryGetValue(key, out string secondValueGot);

            // Assert
            firstValueGot.Should().Be(value);
            firstPresent.Should().BeTrue();
            secondPresent.Should().BeFalse();
        }
    }
}
