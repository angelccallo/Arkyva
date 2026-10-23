using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using Arkyva.Core.Services;

internal sealed class Fixture
{
    internal readonly FakeStorage Storage = new FakeStorage();
    internal readonly FakeMetadata Metadata = new FakeMetadata();
    internal ArkyvaService Service { get { return new ArkyvaService(Storage, Metadata, "FileSystem"); } }
    internal FileMetadata Seed(FileStatus status = FileStatus.Active, string name = "document.pdf")
    {
        var record = new FileMetadata { FileId = Guid.NewGuid(), OriginalName = name, Extension = ".pdf",
            MimeType = "application/pdf", SizeBytes = 3, ProviderId = "FileSystem", StorageKey = Guid.NewGuid().ToString("N") + ".bin",
            CreatedUtc = DateTime.UtcNow.AddDays(-1), Status = status,
            DeletedUtc = status == FileStatus.Deleted ? DateTime.UtcNow.AddHours(-1) : (DateTime?)null };
        Metadata.Rows.Add(record.FileId, FakeMetadata.Copy(record));
        Storage.Files.Add(record.StorageKey, new byte[] { 1, 2, 3 });
        return record;
    }
}

internal sealed class FakeStorage : IStorageProvider
{
    internal readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
    internal int Saves, Reads, Exists, Deletes;
    internal string LastKey;
    internal Exception SaveFailure { get; set; }
    internal Exception ReadFailure { get; set; }
    internal Exception ExistsFailure { get; set; }
    internal bool PublishBeforeFailure { get; set; }
    internal bool Discard { get; set; }
    internal bool DisposeInput { get; set; }
    internal Action OnSave { get; set; }
    internal TrackingStream LastRead;
    internal Stream ReturnedStream { get; set; }
    public async Task SaveAsync(string key, Stream content)
    {
        Saves++; LastKey = key;
        if (OnSave != null) OnSave();
        if (SaveFailure != null && !PublishBeforeFailure) throw SaveFailure;
        if (Discard) await content.CopyToAsync(Stream.Null);
        else
        {
            using (var output = new MemoryStream())
            {
                await content.CopyToAsync(output);
                lock (Files)
                {
                    if (Files.ContainsKey(key)) throw new IOException("Duplicate");
                    Files.Add(key, output.ToArray());
                }
            }
        }
        if (DisposeInput) content.Dispose();
        if (SaveFailure != null) throw SaveFailure;
    }
    public Task<Stream> ReadAsync(string key)
    {
        Reads++;
        if (ReadFailure != null) throw ReadFailure;
        if (ReturnedStream != null) return Task.FromResult(ReturnedStream);
        byte[] bytes;
        if (!Files.TryGetValue(key, out bytes)) throw new FileNotFoundException("private/path/key");
        LastRead = new TrackingStream(bytes);
        return Task.FromResult<Stream>(LastRead);
    }
    public Task<bool> ExistsAsync(string key)
    {
        Exists++;
        if (ExistsFailure != null) throw ExistsFailure;
        return Task.FromResult(Files.ContainsKey(key));
    }
    public Task DeleteAsync(string key) { Deletes++; Files.Remove(key); return Task.CompletedTask; }
}

internal class FakeMetadata : IMetadataRepository
{
    internal readonly Dictionary<Guid, FileMetadata> Rows = new Dictionary<Guid, FileMetadata>();
    internal readonly List<FileMetadata> Writes = new List<FileMetadata>();
    internal int Adds, Gets, Lists, Updates;
    internal FileQuery LastQuery;
    internal Exception AddFailure { get; set; }
    internal Exception GetFailure { get; set; }
    internal Exception ListFailure { get; set; }
    internal Exception UpdateFailure { get; set; }
    internal FileStatus? FailureStatus;
    internal bool CommitAddBeforeFailure { get; set; }
    internal bool CommitUpdateBeforeFailure { get; set; }
    internal Func<Task> OnGet { get; set; }
    internal readonly object sync = new object();
    internal static FileMetadata Copy(FileMetadata m)
    {
        return new FileMetadata { FileId = m.FileId, OriginalName = m.OriginalName, Extension = m.Extension,
            MimeType = m.MimeType, SizeBytes = m.SizeBytes, CreatedUtc = m.CreatedUtc, DeletedUtc = m.DeletedUtc,
            Status = m.Status, ProviderId = m.ProviderId, StorageKey = m.StorageKey, Revision = m.Revision, CompletionSha256 = m.CompletionSha256 };
    }
    public Task AddAsync(FileMetadata m)
    {
        lock (sync)
        {
            Adds++;
            if (AddFailure != null && !CommitAddBeforeFailure) throw AddFailure;
            if (Rows.ContainsKey(m.FileId)) throw new InvalidOperationException("Duplicate");
            Rows.Add(m.FileId, Copy(m)); Writes.Add(Copy(m));
            if (AddFailure != null) throw AddFailure;
        }
        return Task.CompletedTask;
    }
    public async Task<FileMetadata> GetAsync(Guid id)
    {
        FileMetadata result;
        lock (sync)
        {
            Gets++;
            if (GetFailure != null) throw GetFailure;
            FileMetadata record;
            result = Rows.TryGetValue(id, out record) ? Copy(record) : null;
        }
        if (OnGet != null) await OnGet();
        return result;
    }
    public Task<bool> ExistsAsync(Guid id) { throw new Exception("Service must check metadata plus physical availability, not repository Exists alone"); }
    public Task<PagedResult<FileMetadata>> ListAsync(FileQuery query)
    {
        Lists++; LastQuery = query;
        if (ListFailure != null) throw ListFailure;
        lock (sync)
        {
            IEnumerable<FileMetadata> rows = Rows.Values;
            if (query.Status.HasValue) rows = rows.Where(m => m.Status == query.Status);
            if (query.FileId.HasValue) rows = rows.Where(m => m.FileId == query.FileId);
            if (query.OriginalName != null) rows = rows.Where(m => m.OriginalName == query.OriginalName);
            if (query.Extension != null) rows = rows.Where(m => m.Extension == query.Extension);
            if (query.CreatedFromUtc.HasValue) rows = rows.Where(m => m.CreatedUtc >= query.CreatedFromUtc);
            if (query.CreatedBeforeUtc.HasValue) rows = rows.Where(m => m.CreatedUtc < query.CreatedBeforeUtc);
            var all = rows.OrderBy(m => m.CreatedUtc).ThenBy(m => m.FileId).ToArray();
            return Task.FromResult(new PagedResult<FileMetadata> { TotalCount = all.Length,
                Items = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Copy).ToArray() });
        }
    }
    public Task UpdateAsync(FileMetadata m)
    {
        lock (sync)
        {
            Updates++;
            bool fail = UpdateFailure != null && (!FailureStatus.HasValue || FailureStatus == m.Status);
            if (fail && !CommitUpdateBeforeFailure) throw UpdateFailure;
            FileMetadata current;
            if (!Rows.TryGetValue(m.FileId, out current)) throw new InvalidOperationException("Absent");
            // Match the SQL policy, including the one-time size finalization exception.
            bool transition = current.Status == m.Status ||
                current.Status == FileStatus.Pending && (m.Status == FileStatus.Active || m.Status == FileStatus.Failed || m.Status == FileStatus.Deleted) ||
                current.Status == FileStatus.Active && (m.Status == FileStatus.Failed || m.Status == FileStatus.Deleted) ||
                current.Status == FileStatus.Failed && m.Status == FileStatus.Deleted;
            bool size = current.SizeBytes == m.SizeBytes || current.Status == FileStatus.Pending && m.Status == FileStatus.Active;
            if (current.Revision != m.Revision || current.CompletionSha256 != m.CompletionSha256 || !transition || !size || current.CreatedUtc != m.CreatedUtc || current.ProviderId != m.ProviderId || current.StorageKey != m.StorageKey ||
                current.Status == FileStatus.Deleted && current.DeletedUtc != m.DeletedUtc)
                throw new InvalidOperationException("Conflict");
            m.Revision++;
            Rows[m.FileId] = Copy(m); Writes.Add(Copy(m));
            if (fail) throw UpdateFailure;
        }
        return Task.CompletedTask;
    }
}

internal sealed class TrackingStream : MemoryStream
{
    internal bool Disposed;
    internal TrackingStream(byte[] content) : base(content) { }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

internal sealed class GeneratedStream : Stream
{
    private long remaining;
    private readonly bool fail;
    internal bool Disposed;
    internal GeneratedStream(long size, bool fail = false) { remaining = size; this.fail = fail; }
    public override bool CanRead { get { return !Disposed; } }
    public override bool CanSeek { get { return false; } }
    public override bool CanWrite { get { return false; } }
    public override long Length { get { throw new NotSupportedException(); } }
    public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (fail) throw new IOException("Input failed with private/path");
        int read = (int)Math.Min(remaining, count); Array.Clear(buffer, offset, read); remaining -= read; return read;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { return Task.FromResult(Read(buffer, offset, count)); }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    public override void Flush() { throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
    public override void SetLength(long value) { throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
}
