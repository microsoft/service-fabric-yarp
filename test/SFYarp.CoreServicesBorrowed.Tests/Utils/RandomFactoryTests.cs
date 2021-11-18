// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace Yarp.ServiceFabric.CoreServicesBorrowed.Tests
{
    public class RandomFactoryTests
    {
        [Fact]
        public void RandomFactory_Work()
        {
            // Set up the factory.
            var factory = new RandomFactory();

            // Create random class object.
            var random = factory.CreateRandomInstance();

            // Validate.
            random.Should().NotBeNull();
            random.GetType().Should().Be(typeof(RandomWrapper));

            // Validate functionality
            int num = random.Next(5);
            num.Should().BeInRange(0, 5);
        }
    }
}
