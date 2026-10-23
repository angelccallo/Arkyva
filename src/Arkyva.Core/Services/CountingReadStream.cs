using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Arkyva.Core.Services
{
    // Forward-only view: count bytes actually consumed, without taking ownership.
    internal sealed class CountingReadStream : Stream
    {
        private readonly Stream source;
        private bool disposed;
        private readonly SHA256 hash = SHA256.Create();
        private bool eof;
        private string digest;
        internal string CompleteHash()
        {
            if (!eof) throw new IOException("The storage provider did not consume the complete input.");
            if (digest == null)
            {
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                digest = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
            }
            return digest;
        }
        private void Account(byte[] buffer, int offset, int requested, int read)
        {
            if (read > 0) hash.TransformBlock(buffer, offset, read, buffer, offset);
            if (requested > 0 && read == 0) eof = true;
            BytesRead = checked(BytesRead + read);
        }
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
            Account(buffer, offset, count, read);
            return read;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CountingReadStream));
            int read = await source.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            Account(buffer, offset, count, read);
            return read;
        }
        protected override void Dispose(bool disposing) { disposed = true; base.Dispose(disposing); }
        internal void ReleaseHash() { hash.Dispose(); }
        public override void Flush() { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
}
