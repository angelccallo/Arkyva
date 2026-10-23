using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arkyva.Core.Exceptions;

namespace Arkyva.Core.Services
{
    // Hide provider-specific stream types (e.g. FileStream.Name) and sanitize read errors.
    internal sealed class ContentReadStream : Stream
    {
        private readonly Stream source;
        private readonly Guid fileId;
        private bool disposed;
        internal ContentReadStream(Stream source, Guid fileId) { this.source = source; this.fileId = fileId; }
        public override bool CanRead { get { return !disposed && source.CanRead; } }
        public override bool CanSeek { get { return !disposed && source.CanSeek; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return Access(() => source.Length); } }
        public override long Position { get { return Access(() => source.Position); } set { Access(() => source.Position = value); } }
        public override int Read(byte[] buffer, int offset, int count) { return Access(() => source.Read(buffer, offset, count)); }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            CheckDisposed();
            try { return await source.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (ArgumentException) { throw; }
            catch (Exception error) { throw Failure(error); }
        }
        public override long Seek(long offset, SeekOrigin origin) { return Access(() => source.Seek(offset, origin)); }
        public override void Flush() { CheckDisposed(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                try { source.Dispose(); }
                catch (Exception error) { throw Failure(error); }
            }
            base.Dispose(disposing);
        }
        private T Access<T>(Func<T> action)
        {
            CheckDisposed();
            try { return action(); }
            catch (ArgumentException) { throw; }
            catch (NotSupportedException) { throw; }
            catch (Exception error) { throw Failure(error); }
        }
        private void CheckDisposed() { if (disposed) throw new ObjectDisposedException(nameof(ContentReadStream)); }
        private ArkyvaStorageException Failure(Exception error) { return new ArkyvaStorageException("Could not consume the requested content.", fileId, error); }
    }
}
