using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Arkyva.Core.Services
{
    // Forward-only view: count bytes actually consumed, without taking ownership.
    internal sealed class CountingReadStream : Stream
    {
        private readonly Stream source;
        private bool disposed;
        internal long BytesRead { get; private set; }
        internal CountingReadStream(Stream source) { this.source = source; }
        public override bool CanRead { get { return !disposed && source.CanRead; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CountingReadStream));
            int read = source.Read(buffer, offset, count);
            BytesRead = checked(BytesRead + read);
            return read;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CountingReadStream));
            int read = await source.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            BytesRead = checked(BytesRead + read);
            return read;
        }
        protected override void Dispose(bool disposing) { disposed = true; base.Dispose(disposing); }
        public override void Flush() { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
}
