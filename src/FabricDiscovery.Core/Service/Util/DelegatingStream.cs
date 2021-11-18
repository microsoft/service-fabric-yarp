// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Yarp.ServiceFabric.FabricDiscovery.Util
{
    /// <summary>
    /// Forwards all calls to an inner stream except where overridden in a derived class.
    /// </summary>
    internal abstract class DelegatingStream : Stream
    {
        private readonly Stream innerStream;

        protected DelegatingStream(Stream innerStream)
        {
            this.innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        }

        public override bool CanRead
        {
            get { return this.innerStream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return this.innerStream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return this.innerStream.CanWrite; }
        }

        public override long Length
        {
            get { return this.innerStream.Length; }
        }

        public override long Position
        {
            get { return this.innerStream.Position; }
            set { this.innerStream.Position = value; }
        }

        public override int ReadTimeout
        {
            get { return this.innerStream.ReadTimeout; }
            set { this.innerStream.ReadTimeout = value; }
        }

        public override bool CanTimeout
        {
            get { return this.innerStream.CanTimeout; }
        }

        public override int WriteTimeout
        {
            get { return this.innerStream.WriteTimeout; }
            set { this.innerStream.WriteTimeout = value; }
        }

        public override ValueTask DisposeAsync()
        {
            return this.innerStream.DisposeAsync();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.innerStream.Seek(offset, origin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.innerStream.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return this.innerStream.Read(buffer);
        }

        public override int ReadByte()
        {
            return this.innerStream.ReadByte();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return this.innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return this.innerStream.ReadAsync(buffer, cancellationToken);
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return this.innerStream.BeginRead(buffer, offset, count, callback, state);
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return this.innerStream.EndRead(asyncResult);
        }

        public override void Flush()
        {
            this.innerStream.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return this.innerStream.FlushAsync(cancellationToken);
        }

        public override void SetLength(long value)
        {
            this.innerStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.innerStream.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            this.innerStream.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            this.innerStream.WriteByte(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return this.innerStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return this.innerStream.WriteAsync(buffer, cancellationToken);
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return this.innerStream.BeginWrite(buffer, offset, count, callback, state);
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            this.innerStream.EndWrite(asyncResult);
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            return this.innerStream.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.innerStream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
