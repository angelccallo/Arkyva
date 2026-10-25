using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arkyva;
using Arkyva.Core.Exceptions;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using Arkyva.Core.Services;
using Arkyva.Metadata.SqlServer.Repositories;
using Arkyva.Storage.FileSystem;
#if ARKYVA_NETSTANDARD
using Microsoft.Data.SqlClient;
#else
using System.Data.SqlClient;
#endif

internal static partial class Program
{
    private static async Task SynchronousRepositoryChecks(string connectionString)
    {
        var repository = new SqlServerMetadataRepository(connectionString);
        string name = "sync-" + Guid.NewGuid().ToString("N") + ".pdf";
        var first = Metadata(name);
        repository.Add(first);
        var read = repository.Get(first.FileId);
        Check(read.FileId == first.FileId && read.CreatedUtc == Created && read.CreatedUtc.Kind == DateTimeKind.Utc && !repository.Exists(first.FileId), "Sync SQL insert/get preserves UTC and Pending semantics");
        Check((await repository.GetAsync(first.FileId)).StorageKey == read.StorageKey, "Sync SQL insertion is visible to Async queries");
        Check(repository.Get(Guid.NewGuid()) == null && !repository.Exists(Guid.NewGuid()), "Sync SQL missing queries");
        try { repository.Add(first); throw new Exception("Expected duplicate"); }
        catch (SqlException error) { Check(error.Number == 2601 || error.Number == 2627, "Sync SQL duplicate is rejected"); }
        Check(repository.GetByStorageKey(first.ProviderId, first.StorageKey).FileId == first.FileId, "Sync exact technical storage-key lookup");
        var stale = repository.Get(first.FileId);
        Check(repository.TryRecordUpload(first, 123, new string('a', 64)) && !repository.TryRecordUpload(stale, 123, new string('a', 64)), "Sync SQL receipt uses revision compare-and-swap");
        first.Status = FileStatus.Active;
        repository.Update(first);
        Check(repository.Exists(first.FileId) && await repository.ExistsAsync(first.FileId), "Sync SQL activation interoperates with Async existence");
        var second = Metadata(name);
        repository.Add(second);
        Check(repository.TryRecordUpload(second, 123, new string('b', 64)) && repository.TryActivateRecovered(second), "Sync metadata recovery activation accepts an observed valid receipt");
        var page = repository.List(new FileQuery { OriginalName = name, PageSize = 1, CreatedFromUtc = Created, CreatedBeforeUtc = Created.AddSeconds(1) });
        Check(page.TotalCount == 2 && page.Items.Count == 1 && repository.List(new FileQuery { OriginalName = name, PageSize = 1, PageNumber = 2 }).Items.Count == 1, "Sync SQL stable pagination, name, state and date filters");
        Check(repository.List(new FileQuery { OriginalName = "' OR 1=1--" }).TotalCount == 0, "Sync SQL treats injection input as a literal parameter");
        var left = repository.Get(first.FileId); var right = await repository.GetAsync(first.FileId);
        left.Status = FileStatus.Failed; right.Status = FileStatus.Deleted; right.DeletedUtc = Created.AddHours(1);
        var writes = await Task.WhenAll(Task.Run(() =>
        {
            try { repository.Update(left); return true; } catch (InvalidOperationException) { return false; }
        }), UpdateAsyncOutcome(repository, right));
        Check(writes.Count(x => x) == 1 && repository.Get(first.FileId).Revision == 3, "Mixed sync/async SQL state updates have one revision winner");
        second.Status = FileStatus.Deleted; second.DeletedUtc = Created.AddHours(1); repository.Update(second);
        Check(!repository.Exists(second.FileId) && repository.Get(second.FileId).DeletedUtc == second.DeletedUtc, "Sync SQL logical deletion records UTC");
        try { var invalid = repository.Get(second.FileId); invalid.Status = FileStatus.Active; invalid.DeletedUtc = null; repository.Update(invalid); throw new Exception("Expected terminal state conflict"); }
        catch (InvalidOperationException) { Check(true, "Sync SQL rejects Deleted to Active transition"); }
        var recovering = Metadata("sync-recovery.bin"); repository.Add(recovering);
        repository.TryRecordUpload(recovering, 2, new string('c', 64));
        var a = repository.Get(recovering.FileId); var b = repository.Get(recovering.FileId);
        var recovered = await Task.WhenAll(Task.Run(() => repository.TryActivateRecovered(a)), repository.TryActivateRecoveredAsync(b));
        Check(recovered.Count(x => x) == 1, "Mixed synchronous/asynchronous receipt recovery has one winner");
    }

    private static async Task<bool> UpdateAsyncOutcome(SqlServerMetadataRepository repository, FileMetadata record)
    { try { await repository.UpdateAsync(record); return true; } catch (InvalidOperationException) { return false; } }

    private static async Task SynchronousEndToEndChecks(IArkyvaService service, SqlServerMetadataRepository repository, string root)
    {
        byte[] payload = { 1, 2, 3, 0, 255 };
        Guid syncId;
        using (var source = new MemoryStream(payload)) syncId = service.Upload(source, "synchronous.bin");
        using (var content = await service.DownloadAsync(syncId))
        using (var copy = new MemoryStream()) { await content.Content.CopyToAsync(copy); Check(copy.ToArray().SequenceEqual(payload), "Real SQL/FileSystem Upload to DownloadAsync roundtrip"); }
        Guid asyncId;
        using (var source = new MemoryStream(payload)) asyncId = await service.UploadAsync(source, "asynchronous.bin");
        using (var content = service.Download(asyncId))
        using (var copy = new MemoryStream()) { content.Content.CopyTo(copy); Check(copy.ToArray().SequenceEqual(payload), "Real SQL/FileSystem UploadAsync to Download roundtrip"); }
        Check(service.GetInfo(syncId).SizeBytes == (await service.GetInfoAsync(syncId)).SizeBytes && service.Exists(syncId), "Real sync GetInfo and Exists match Async");
        Check(service.List(new FileQuery { FileId = syncId, PageSize = 1 }).TotalCount == 1, "Real sync public paginated List");
        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? Task.Run(() =>
        {
            using (var source = new MemoryStream(payload)) return service.Upload(source, "mixed.bin");
        }) : UploadOneAsync(service, payload)));
        Check(ids.Distinct().Count() == 8 && ids.Select(repository.Get).All(m => m.Status == FileStatus.Active && m.SizeBytes == payload.Length && m.CompletionSha256 != null), "Real mixed concurrent uploads retain unique complete Active records");
        service.Delete(syncId); service.Delete(syncId);
        Check(repository.Get(syncId).Status == FileStatus.Deleted && repository.Get(syncId).DeletedUtc.HasValue && service.GetInfo(syncId) == null && !await service.ExistsAsync(syncId), "Real sync Delete is logical, UTC and idempotent");
        try { service.Download(syncId); throw new Exception("Expected unavailable"); } catch (ArkyvaFileUnavailableException) { Check(true, "Real sync deleted download rejected"); }
        await service.DeleteAsync(asyncId);
        Check(!service.Exists(asyncId) && new FileSystemStorageProvider(root).Exists(repository.Get(syncId).StorageKey), "Async Delete observed by sync API retains physical content");
        var bad = new SqlConnectionStringBuilder(repositoryConnection(repository)) { InitialCatalog = "ArkyvaMissing_" + Guid.NewGuid().ToString("N") };
        var broken = ArkyvaBuilder.Create().UseSqlServer(bad.ConnectionString).UseFileSystem(root).Build();
        int before = Directory.GetFiles(root, "*.bin", SearchOption.AllDirectories).Length;
        using (var source = new MemoryStream(payload))
        {
            try { broken.Upload(source, "connection-failure.bin"); throw new Exception("Expected metadata failure"); }
            catch (ArkyvaMetadataException error) { Check(error.InnerException is SqlException && error.FileId.HasValue && repository.Get(error.FileId.Value) == null && before == Directory.GetFiles(root, "*.bin", SearchOption.AllDirectories).Length, "Real sync SQL connection failure is wrapped and prevents physical storage"); }
        }
        var pending = Metadata("native-interruption.bin"); pending.CreatedUtc = DateTime.UtcNow.AddHours(-3);
        repository.Add(pending);
        var storage = new FileSystemStorageProvider(root);
        using (var source = new MemoryStream(payload)) storage.Save(pending.StorageKey, source);
        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", "").ToLowerInvariant();
        Check(repository.TryRecordUpload(pending, payload.Length, hash), "Native physical publication has a durable synchronous receipt");
        var recovery = new StorageRecoveryService(storage, repository, "FileSystem");
        var result = await recovery.RecoverAsync(new RecoveryOptions { DryRun = false, InspectStorage = false });
        Check(result.RecordsRecovered == 1 && service.Exists(pending.FileId), "Real Async recovery verifies content and receipt produced synchronously");
        Check((await recovery.RecoverAsync(new RecoveryOptions { DryRun = false, InspectStorage = false })).RecordsRecovered == 0, "Real recovery remains idempotent after native publication");
    }
    private static string repositoryConnection(SqlServerMetadataRepository repository)
    { return (string)typeof(SqlServerMetadataRepository).GetField("connectionString", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(repository); }
    private static async Task<Guid> UploadOneAsync(IArkyvaService service, byte[] payload)
    { using (var source = new MemoryStream(payload)) return await service.UploadAsync(source, "mixed.bin"); }
}
