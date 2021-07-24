﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace IslandGateway.CoreServicesBorrowed.Tests
{
    public class ThreadStaticRandomTests
    {
        [Fact]
        public void ThreadStaticRandom_Work()
        {
            // Set up the random instance.
            var random = ThreadStaticRandom.Instance;

            // Validate.
            random.Should().NotBeNull();
            random.GetType().Should().Be(typeof(RandomWrapper));

            // Validate random generation.
            var num = random.Next();
            num.Should().BeGreaterOrEqualTo(0);
            num = random.Next(5);
            num.Should().BeInRange(0, 5);
            num = random.Next(0, 5);
            num.Should().BeInRange(0, 5);
        }
    }
}
