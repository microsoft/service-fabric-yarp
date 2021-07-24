// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace IslandGateway.FabricDiscovery.Util.Tests
{
    public class DictionaryUtilTests
    {
        [Fact]
        public void CombineDictionaries_Works()
        {
            // Arrange
            var dict1 = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" },
            };
            var dict2 = new Dictionary<string, string>
            {
                { "key2", "value2-changed" },
                { "kEy2", "value3" },
            };

            // Act
            var result = DictionaryUtil.CombineDictionaries(dict1, dict2, StringComparer.Ordinal);

            // Assert
            result.Should().HaveCount(3);
            result["key1"].Should().Be("value1");
            result["key2"].Should().Be("value2-changed");
            result["kEy2"].Should().Be("value3");

            dict1.Should().HaveCount(2);
            dict2.Should().HaveCount(2);
        }

        [Fact]
        public void ShallowClone_Works()
        {
            // Arrange
            var source = new Dictionary<string, object>
            {
                { "key1", new object() },
                { "key2", new object() },
                { "Key2", new object() },
            };

            // Act
            var result = source.ShallowClone();

            // Asssert
            result.Should().NotBeSameAs(source);
            result.Should().HaveCount(3);
            result["key1"].Should().BeSameAs(source["key1"]);
            result["key2"].Should().BeSameAs(source["key2"]);
            result["Key2"].Should().BeSameAs(source["Key2"]);
        }
    }
}