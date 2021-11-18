// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Yarp.ServiceFabric.FabricDiscovery.Util.Tests
{
    public class WriteLengthStreamTests
    {
        [Fact]
        public void Constructor_Works()
        {
            using (var ms = new MemoryStream())
            using (var sut = new WriteLengthStream(ms))
            {
                sut.BytesWritten.Should().Be(0);
            }
        }

        [Fact]
        public void Read_Works()
        {
            using (var ms = new MemoryStream(new byte[] { 1, 2, 3 }))
            using (var sut = new WriteLengthStream(ms))
            {
                var buf = new byte[3];
                sut.Read(buf, 1, 2);
                buf.Should().Equal(new byte[] { 0, 1, 2 });
                sut.BytesWritten.Should().Be(0);
            }
        }

        [Fact]
        public void WriteByte_Works()
        {
            using (var ms = new MemoryStream())
            using (var sut = new WriteLengthStream(ms))
            {
                sut.WriteByte(254);
                ms.ToArray().Should().Equal(new byte[] { 254 });
                sut.BytesWritten.Should().Be(1);

                sut.WriteByte(255);
                ms.ToArray().Should().Equal(new byte[] { 254, 255 });
                sut.BytesWritten.Should().Be(2);
            }
        }

        [Fact]
        public void Write1_Works()
        {
            using (var ms = new MemoryStream())
            using (var sut = new WriteLengthStream(ms))
            {
                sut.Write(new byte[] { 0, 1, 2 }, 1, 2);
                ms.ToArray().Should().Equal(new byte[] { 1, 2 });
                sut.BytesWritten.Should().Be(2);

                sut.Write(new byte[] { 3, 4 }, 0, 2);
                ms.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4 });
                sut.BytesWritten.Should().Be(4);
            }
        }

        [Fact]
        public void Write2_Works()
        {
            using (var ms = new MemoryStream())
            using (var sut = new WriteLengthStream(ms))
            {
                sut.Write(new byte[] { 0, 1, 2 }.AsSpan(1, 2));
                ms.ToArray().Should().Equal(new byte[] { 1, 2 });
                sut.BytesWritten.Should().Be(2);

                sut.Write(new byte[] { 3, 4 }.AsSpan());
                ms.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4 });
                sut.BytesWritten.Should().Be(4);
            }
        }

        [Fact]
        public async Task WriteAsync1_Works()
        {
            using (var ms = new MemoryStream())
            using (var sut = new WriteLengthStream(ms))
            {
                await sut.WriteAsync(new byte[] { 0, 1, 2 }, 1, 2);
                ms.ToArray().Should().Equal(new byte[] { 1, 2 });
                sut.BytesWritten.Should().Be(2);

                await sut.WriteAsync(new byte[] { 3, 4 }, 0, 2);
                ms.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4 });
                sut.BytesWritten.Should().Be(4);
            }
        }

        [Fact]
        public async Task WriteAsync2_Works()
        {
            using (var ms = new MemoryStream())
            using (var sut = new WriteLengthStream(ms))
            {
                await sut.WriteAsync(new byte[] { 0, 1, 2 }, 1, 2, CancellationToken.None);
                ms.ToArray().Should().Equal(new byte[] { 1, 2 });
                sut.BytesWritten.Should().Be(2);

                await sut.WriteAsync(new byte[] { 3, 4 }, 0, 2, CancellationToken.None);
                ms.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4 });
                sut.BytesWritten.Should().Be(4);
            }
        }

        [Fact]
        public async Task WriteAsync3_Works()
        {
            using (var ms = new MemoryStream())
            using (var sut = new WriteLengthStream(ms))
            {
                await sut.WriteAsync(new byte[] { 0, 1, 2 }.AsMemory(1, 2));
                ms.ToArray().Should().Equal(new byte[] { 1, 2 });
                sut.BytesWritten.Should().Be(2);

                await sut.WriteAsync(new byte[] { 3, 4 }.AsMemory());
                ms.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4 });
                sut.BytesWritten.Should().Be(4);
            }
        }

        [Fact]
        public async Task BeginWrite_EndWrite_Works()
        {
            var taskFactory = new TaskFactory();

            using (var ms = new MemoryStream())
            using (var sut = new WriteLengthStream(ms))
            {
                await taskFactory.FromAsync(sut.BeginWrite, sut.EndWrite, new byte[] { 0, 1, 2 }, 1, 2, state: null);
                ms.ToArray().Should().Equal(new byte[] { 1, 2 });
                sut.BytesWritten.Should().Be(2);

                await taskFactory.FromAsync(sut.BeginWrite, sut.EndWrite, new byte[] { 3, 4 }, 0, 2, state: null);
                ms.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4 });
                sut.BytesWritten.Should().Be(4);
            }
        }
    }
}
