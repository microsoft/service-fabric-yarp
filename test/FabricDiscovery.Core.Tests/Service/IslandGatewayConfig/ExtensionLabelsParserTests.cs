// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Yarp.ServiceFabric.FabricDiscovery.IslandGatewayConfig.Tests
{
    public class ExtensionLabelsParserTests : TestAutoMockBase
    {
        private readonly ExtensionLabelsParser sut;

        public ExtensionLabelsParserTests()
        {
            this.sut = this.Create<ExtensionLabelsParser>();
        }

        [Fact]
        public void TryExtractLabels_NoLabelEntries_ReturnsTrue()
        {
            // Arrange
            var xml = "<Labels xmlns=\"http://schemas.microsoft.com/2015/03/fabact-no-schema\"></Labels>";

            // Act
            var result = this.sut.TryExtractLabels(xml, out var labels);

            // Assert
            result.Should().BeTrue();
            labels.Should().BeEmpty();
        }

        [Fact]
        public void TryExtractLabels_FewLabels_ReturnsTrue()
        {
            // Arrange
            var xml = @"
<Labels xmlns=""http://schemas.microsoft.com/2015/03/fabact-no-schema"">
  <Label Key=""key1"">value1</Label>
  <Label Key=""kEy1"">value2</Label>
</Labels>";

            // Act
            var result = this.sut.TryExtractLabels(xml, out var labels);

            // Assert
            result.Should().BeTrue();
            labels.Should().HaveCount(2);
            labels["key1"].Should().Be("value1");
            labels["kEy1"].Should().Be("value2");
        }

        [Fact]
        public void TryExtractLabels_NoLabelsElement_ReturnsTrue()
        {
            // Arrange
            var xml = @"<Anything><Label Key=""key1"">value1</Label></Anything>";

            // Act
            var result = this.sut.TryExtractLabels(xml, out var labels);

            // Assert
            result.Should().BeTrue();
            labels.Should().BeEmpty();
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("{}")]
        [InlineData("")]
        [InlineData("<Labels xmlns=\"http://schemas.microsoft.com/2015/03/fabact-no-schema\"></Labels")]
        public void TryExtractLabels_InvalidXml_ReturnsFalse(string xml)
        {
            // Act
            var result = this.sut.TryExtractLabels(xml, out var labels);

            // Assert
            result.Should().BeFalse();
            labels.Should().BeNull();
        }
    }
}