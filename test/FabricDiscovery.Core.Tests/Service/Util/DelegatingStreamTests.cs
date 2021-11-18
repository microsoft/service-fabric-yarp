// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Yarp.ServiceFabric.FabricDiscovery.Util.Tests
{
    public class DelegatingStreamTests
    {
        [Fact]
        public void Constructor_Works()
        {
            using (var ms = new MemoryStream())
            using (var sut = new TestDelegatingStream(ms))
            {
            }
        }

        [Fact]
        public void Read_Works()
        {
            using (var ms = new MemoryStream(new byte[] { 1, 2, 3 }))
            using (var sut = new TestDelegatingStream(ms))
            {
                var buf = new byte[3];
                sut.Read(buf, 1, 2);
                buf.Should().Equal(new byte[] { 0, 1, 2 });
            }
        }

        [Fact]
        public void Write_Works()
        {
            using (var ms = new MemoryStream())
            using (var sut = new TestDelegatingStream(ms))
            {
                sut.Write(new byte[] { 0, 1, 2 }, 1, 2);
                ms.ToArray().Should().Equal(new byte[] { 1, 2 });
            }
        }

        private class TestDelegatingStream : DelegatingStream
        {
            public TestDelegatingStream(Stream innerStream)
                : base(innerStream)
            {
            }
        }
    }
}
