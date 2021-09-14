// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using FluentAssertions;
using IslandGateway.ServiceFabricIntegration;
using Xunit;

namespace IslandGateway.FabricDiscovery.Util.Tests
{
    public class FabricExponentialRetryPolicyTests
    {
        [Fact]
        public void RetryPolicy_ValidatesParams()
        {
            // Arrange
            var fabricRetryPolicy = new FabricExponentialRetryPolicy
            {
                NumAttempts = 3,
                InitialBackoffMs = 1000,
                MaxBackoffMs = 15000,
            };

            // + Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => fabricRetryPolicy.IsRetryAllowed(0, out var _));
            Assert.Throws<ArgumentOutOfRangeException>(() => fabricRetryPolicy.IsRetryAllowed(-100, out var _));
        }

        [Fact]
        public void RetryPolicy_CorrectlyCalculatesBackoff()
        {
            // Arrange
            var fabricRetryPolicy = new FabricExponentialRetryPolicy
            {
                NumAttempts = 6,
                InitialBackoffMs = 1000,
                MaxBackoffMs = 15000,
            };

            // Act + Assert
            fabricRetryPolicy.IsRetryAllowed(1, out int backoff).Should().Be(true);
            backoff.Should().Be(1000);
            fabricRetryPolicy.IsRetryAllowed(2, out backoff).Should().Be(true);
            backoff.Should().Be(3000);
            fabricRetryPolicy.IsRetryAllowed(3, out backoff).Should().Be(true);
            backoff.Should().Be(7000);
            fabricRetryPolicy.IsRetryAllowed(4, out backoff).Should().Be(true);
            backoff.Should().Be(15000);
            fabricRetryPolicy.IsRetryAllowed(5, out backoff).Should().Be(true);
            backoff.Should().Be(15000); // reached max backoff
            fabricRetryPolicy.IsRetryAllowed(6, out backoff).Should().Be(false);
            backoff.Should().Be(default);
        }
    }
}
