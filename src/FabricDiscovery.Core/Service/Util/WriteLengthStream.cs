// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Yarp.ServiceFabric.FabricDiscovery.Util
{
    /// <summary>
    /// Forwards all calls to an inner stream, keeping track of how many bytes are written in total.
    /// </summary>
    internal sealed class WriteLengthStream : DelegatingStream
    {
        private long bytesWritten;

        public WriteLengthStream(Stream innerStream)
            : base(innerStream)
        {
        }

        public long BytesWritten => this.bytesWritten;

        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            this.bytesWritten += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            base.Write(buffer);
            this.bytesWritten += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            base.WriteByte(value);
            this.bytesWritten++;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var result = base.WriteAsync(buffer, offset, count, cancellationToken);
            this.bytesWritten += count;
            return result;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = base.WriteAsync(buffer, cancellationToken);
            this.bytesWritten += buffer.Length;
            return result;
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            var result = base.BeginWrite(buffer, offset, count, callback, state);
            this.bytesWritten += count;
            return result;
        }
    }
}
