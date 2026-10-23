using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using Arkyva.Core.Services;

internal sealed class ReceiptMetadata : FakeMetadata, IRecoveryMetadataRepository
{
    internal int ReceiptCalls, RecoveryCalls;
    internal Exception ReceiptFailure;
    internal bool CommitReceiptBeforeFailure;
    internal Func<Task> BeforeActivate;
    public new Task UpdateAsync(FileMetadata expected)
    {
        lock (sync)
        {
            FileMetadata current;
            if (Rows.TryGetValue(expected.FileId, out current) &&
                (current.SizeBytes != expected.SizeBytes || current.Status == FileStatus.Pending && expected.Status == FileStatus.Active && current.CompletionSha256 == null))
                throw new InvalidOperationException("A receipt must finalize size before activation.");
            return base.UpdateAsync(expected);
        }
    }
    public Task<bool> TryRecordUploadAsync(FileMetadata expected, long sizeBytes, string sha256)
    {
        lock (sync)
        {
            ReceiptCalls++;
            if (ReceiptFailure != null && !CommitReceiptBeforeFailure) throw ReceiptFailure;
            FileMetadata row;
            if (!Rows.TryGetValue(expected.FileId, out row) || row.Revision != expected.Revision || row.Status != FileStatus.Pending || row.CompletionSha256 != null)
                return Task.FromResult(false);
            row.SizeBytes = sizeBytes; row.CompletionSha256 = sha256; row.Revision++;
            expected.SizeBytes = sizeBytes; expected.CompletionSha256 = sha256; expected.Revision = row.Revision;
            Writes.Add(Copy(row));
            if (ReceiptFailure != null) throw ReceiptFailure;
            return Task.FromResult(true);
        }
    }
    public async Task<bool> TryActivateRecoveredAsync(FileMetadata expected)
    {
        if (BeforeActivate != null) await BeforeActivate();
        lock (sync)
        {
            RecoveryCalls++;
            FileMetadata row;
            if (!Rows.TryGetValue(expected.FileId, out row) || row.Revision != expected.Revision || row.Status != expected.Status ||
                row.CompletionSha256 == null || row.CompletionSha256 != expected.CompletionSha256 || row.SizeBytes != expected.SizeBytes ||
                row.Status != FileStatus.Pending && row.Status != FileStatus.Failed) return false;
            row.Status = FileStatus.Active; row.Revision++;
            expected.Status = row.Status; expected.Revision = row.Revision; Writes.Add(Copy(row)); return true;
        }
    }
    public Task<FileMetadata> GetByStorageKeyAsync(string providerId, string storageKey)
    {
        lock (sync)
        {
            if (GetFailure != null) throw GetFailure;
            var row = Rows.Values.SingleOrDefault(x => x.ProviderId == providerId && x.StorageKey == storageKey);
            return Task.FromResult(row == null ? null : Copy(row));
        }
    }
}

internal sealed class RecoveryFixture
{
    internal readonly FakeStorage Storage = new FakeStorage();
    internal readonly ReceiptMetadata Metadata = new ReceiptMetadata();
    internal ArkyvaService Service { get { return new ArkyvaService(Storage, Metadata, "FileSystem"); } }
    internal StorageRecoveryService Recovery { get { return new StorageRecoveryService(Storage, Metadata, "FileSystem"); } }
    internal FileMetadata Seed(FileStatus status, bool content = true, bool receipt = true)
    {
        byte[] bytes = new byte[] { 1, 2, 3 };
        var row = new FileMetadata { FileId = Guid.NewGuid(), OriginalName = "document.bin", Extension = ".bin", MimeType = "application/octet-stream",
            ProviderId = "FileSystem", StorageKey = Guid.NewGuid().ToString("N") + ".bin", SizeBytes = 3, Status = status,
            CreatedUtc = DateTime.UtcNow.AddDays(-2), CompletionSha256 = receipt ? Hash(bytes) : null,
            DeletedUtc = status == FileStatus.Deleted ? DateTime.UtcNow.AddDays(-1) : (DateTime?)null };
        Metadata.Rows.Add(row.FileId, FakeMetadata.Copy(row)); if (content) Storage.Files.Add(row.StorageKey, bytes);
        return row;
    }
    internal static string Hash(byte[] bytes)
    { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
}

internal sealed class GatedStorage : IStorageProvider
{
    internal readonly FakeStorage Inner = new FakeStorage();
    internal readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task SaveAsync(string key, Stream content) { Entered.TrySetResult(true); await Release.Task; await Inner.SaveAsync(key, content); }
    public Task<Stream> ReadAsync(string key) { return Inner.ReadAsync(key); }
    public Task<bool> ExistsAsync(string key) { return Inner.ExistsAsync(key); }
    public Task DeleteAsync(string key) { throw new Exception("Recovery must not delete"); }
}

internal sealed class IncompleteStorage : IStorageProvider
{
    public async Task SaveAsync(string key, Stream content) { byte[] one = new byte[1]; await content.ReadAsync(one, 0, 1); }
    public Task<Stream> ReadAsync(string key) { throw new NotSupportedException(); }
    public Task<bool> ExistsAsync(string key) { return Task.FromResult(true); }
    public Task DeleteAsync(string key) { throw new Exception("Must not delete"); }
}
