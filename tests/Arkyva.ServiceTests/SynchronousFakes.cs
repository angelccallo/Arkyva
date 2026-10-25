using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using Arkyva.Storage.FileSystem;

internal sealed class SynchronousMetadata : FakeMetadata, ISynchronousRecoveryMetadataRepository
{
    internal bool DenyAsync = true;
    internal int AsyncCalls;
    internal Exception ReceiptFailure;
    internal bool CommitReceiptBeforeFailure;
    public void Add(FileMetadata record)
    {
        lock (sync)
        {
            Adds++;
            if (AddFailure != null && !CommitAddBeforeFailure) throw AddFailure;
            Rows.Add(record.FileId, Copy(record)); Writes.Add(Copy(record));
            if (AddFailure != null) throw AddFailure;
        }
    }
    public FileMetadata Get(Guid id)
    {
        lock (sync) { Gets++; if (GetFailure != null) throw GetFailure; FileMetadata row; return Rows.TryGetValue(id, out row) ? Copy(row) : null; }
    }
    public PagedResult<FileMetadata> List(FileQuery query)
    {
        lock (sync)
        {
            Lists++; LastQuery = query;
            if (ListFailure != null) throw ListFailure;
            var rows = Rows.Values.Where(m => (!query.Status.HasValue || m.Status == query.Status) &&
                (!query.FileId.HasValue || m.FileId == query.FileId) &&
                (query.OriginalName == null || m.OriginalName == query.OriginalName) &&
                (query.Extension == null || m.Extension == query.Extension) &&
                (!query.CreatedFromUtc.HasValue || m.CreatedUtc >= query.CreatedFromUtc) &&
                (!query.CreatedBeforeUtc.HasValue || m.CreatedUtc < query.CreatedBeforeUtc))
                .OrderBy(m => m.CreatedUtc).ThenBy(m => m.FileId).ToArray();
            return new PagedResult<FileMetadata> { TotalCount = rows.Length,
                Items = rows.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Copy).ToArray() };
        }
    }
    public bool Exists(Guid id) { var row = Get(id); return row != null && row.Status == FileStatus.Active; }
    public void Update(FileMetadata record)
    {
        lock (sync)
        {
            Updates++;
            bool fail = UpdateFailure != null && (!FailureStatus.HasValue || FailureStatus == record.Status);
            if (fail && !CommitUpdateBeforeFailure) throw UpdateFailure;
            FileMetadata row;
            if (!Rows.TryGetValue(record.FileId, out row) || row.Revision != record.Revision ||
                row.CompletionSha256 != record.CompletionSha256 || row.SizeBytes != record.SizeBytes ||
                row.StorageKey != record.StorageKey || row.ProviderId != record.ProviderId || row.CreatedUtc != record.CreatedUtc ||
                row.Status == FileStatus.Deleted && record.Status != FileStatus.Deleted ||
                record.Status == FileStatus.Active && row.CompletionSha256 == null)
                throw new InvalidOperationException("Revision or evidence conflict");
            record.Revision++; Rows[record.FileId] = Copy(record); Writes.Add(Copy(record));
            if (fail) throw UpdateFailure;
        }
    }
    public bool TryRecordUpload(FileMetadata expected, long sizeBytes, string sha256)
    {
        lock (sync)
        {
            if (ReceiptFailure != null && !CommitReceiptBeforeFailure) throw ReceiptFailure;
            FileMetadata row;
            if (!Rows.TryGetValue(expected.FileId, out row) || row.Revision != expected.Revision || row.Status != FileStatus.Pending || row.CompletionSha256 != null) return false;
            row.SizeBytes = sizeBytes; row.CompletionSha256 = sha256; row.Revision++;
            expected.SizeBytes = sizeBytes; expected.CompletionSha256 = sha256; expected.Revision = row.Revision;
            Writes.Add(Copy(row));
            if (ReceiptFailure != null) throw ReceiptFailure;
            return true;
        }
    }
    public bool TryActivateRecovered(FileMetadata expected)
    {
        lock (sync)
        {
            FileMetadata row;
            if (!Rows.TryGetValue(expected.FileId, out row) || row.Revision != expected.Revision || row.Status != expected.Status ||
                row.CompletionSha256 == null || row.CompletionSha256 != expected.CompletionSha256 || row.SizeBytes != expected.SizeBytes) return false;
            row.Status = FileStatus.Active; row.Revision++; expected.Status = row.Status; expected.Revision = row.Revision;
            return true;
        }
    }
    public FileMetadata GetByStorageKey(string provider, string key)
    { lock (sync) { var row = Rows.Values.FirstOrDefault(m => m.ProviderId == provider && m.StorageKey == key); return row == null ? null : Copy(row); } }
    private void AsyncAllowed() { AsyncCalls++; if (DenyAsync) throw new Exception("Synchronous service called an Async metadata operation"); }
    Task IMetadataRepository.AddAsync(FileMetadata m) { AsyncAllowed(); Add(m); return Task.CompletedTask; }
    Task<FileMetadata> IMetadataRepository.GetAsync(Guid id) { AsyncAllowed(); return Task.FromResult(Get(id)); }
    Task<PagedResult<FileMetadata>> IMetadataRepository.ListAsync(FileQuery query) { AsyncAllowed(); return Task.FromResult(List(query)); }
    Task<bool> IMetadataRepository.ExistsAsync(Guid id) { AsyncAllowed(); return Task.FromResult(Exists(id)); }
    Task IMetadataRepository.UpdateAsync(FileMetadata m) { AsyncAllowed(); Update(m); return Task.CompletedTask; }
    public Task<bool> TryRecordUploadAsync(FileMetadata m, long size, string hash) { AsyncAllowed(); return Task.FromResult(TryRecordUpload(m, size, hash)); }
    public Task<bool> TryActivateRecoveredAsync(FileMetadata m) { AsyncAllowed(); return Task.FromResult(TryActivateRecovered(m)); }
    public Task<FileMetadata> GetByStorageKeyAsync(string provider, string key) { AsyncAllowed(); return Task.FromResult(GetByStorageKey(provider, key)); }
}

internal sealed class SynchronousStorage : ISynchronousStorageProvider
{
    internal readonly FileSystemStorageProvider Inner;
    internal bool DenyAsync = true, PublishBeforeFailure, Incomplete;
    internal int AsyncCalls, Saves, Deletes;
    internal Exception SaveFailure, ReadFailure;
    internal Stream LastRead;
    internal SynchronousStorage(string root) { Inner = new FileSystemStorageProvider(root); }
    public void Save(string key, Stream content)
    {
        Saves++;
        if (SaveFailure != null && !PublishBeforeFailure) throw SaveFailure;
        if (Incomplete)
        {
            byte[] first = new byte[1]; content.Read(first, 0, 1);
            using (var truncated = new MemoryStream(first)) Inner.Save(key, truncated);
        }
        else Inner.Save(key, content);
        if (SaveFailure != null) throw SaveFailure;
    }
    public Stream Read(string key) { if (ReadFailure != null) throw ReadFailure; return LastRead = Inner.Read(key); }
    public bool Exists(string key) { return Inner.Exists(key); }
    public void Delete(string key) { Deletes++; Inner.Delete(key); }
    private void AsyncAllowed() { AsyncCalls++; if (DenyAsync) throw new Exception("Synchronous service called an Async physical operation"); }
    public Task SaveAsync(string key, Stream content) { AsyncAllowed(); return Inner.SaveAsync(key, content); }
    public Task<Stream> ReadAsync(string key) { AsyncAllowed(); return Inner.ReadAsync(key); }
    public Task<bool> ExistsAsync(string key) { AsyncAllowed(); return Inner.ExistsAsync(key); }
    public Task DeleteAsync(string key) { AsyncAllowed(); Deletes++; return Inner.DeleteAsync(key); }
}

internal sealed class AsyncRecoveryOnlyMetadata : ISynchronousMetadataRepository, IRecoveryMetadataRepository
{
    internal readonly SynchronousMetadata Inner = new SynchronousMetadata();
    public void Add(FileMetadata row) { Inner.Add(row); }
    public FileMetadata Get(Guid id) { return Inner.Get(id); }
    public PagedResult<FileMetadata> List(FileQuery query) { return Inner.List(query); }
    public bool Exists(Guid id) { return Inner.Exists(id); }
    public void Update(FileMetadata row) { Inner.Update(row); }
    public Task AddAsync(FileMetadata row) { throw new Exception("Unexpected async call"); }
    public Task<FileMetadata> GetAsync(Guid id) { throw new Exception("Unexpected async call"); }
    public Task<PagedResult<FileMetadata>> ListAsync(FileQuery query) { throw new Exception("Unexpected async call"); }
    public Task<bool> ExistsAsync(Guid id) { throw new Exception("Unexpected async call"); }
    public Task UpdateAsync(FileMetadata row) { throw new Exception("Unexpected async call"); }
    public Task<bool> TryRecordUploadAsync(FileMetadata expected, long size, string hash) { throw new Exception("Unexpected async call"); }
    public Task<bool> TryActivateRecoveredAsync(FileMetadata expected) { throw new Exception("Unexpected async call"); }
    public Task<FileMetadata> GetByStorageKeyAsync(string provider, string key) { throw new Exception("Unexpected async call"); }
}
