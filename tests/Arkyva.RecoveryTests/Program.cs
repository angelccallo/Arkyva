using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arkyva.Core.Exceptions;
using Arkyva.Core.Models;
using Arkyva.Core.Services;
using Arkyva.Storage.FileSystem;

internal static class Program
{
    private static int passed;
    private static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine(passed + " recovery tests passed (no SQL server)."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static RecoveryOptions Options(bool dry = false) { return new RecoveryOptions { DryRun = dry, InspectStorage = false }; }
    private static async Task Run()
    {
        await Test("Successful upload persists receipt between publication and activation", async () =>
        {
            var f = new RecoveryFixture(); Guid id = await f.Service.UploadAsync(new MemoryStream(new byte[] { 97, 98, 99 }), "a.bin");
            var row = f.Metadata.Rows[id];
            Check(row.CompletionSha256 == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad" && row.SizeBytes == 3 && row.Revision == 2);
            Check(f.Metadata.Writes.Select(x => x.Status).SequenceEqual(new[] { FileStatus.Pending, FileStatus.Pending, FileStatus.Active }));
        });
        await Test("A provider returning before EOF cannot produce Active or a receipt", async () =>
        {
            var metadata = new ReceiptMetadata();
            var service = new ArkyvaService(new IncompleteStorage(), metadata, "FileSystem");
            await Throws<ArkyvaStorageException>(() => service.UploadAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "x"));
            Check(metadata.Rows.Values.Single().Status == FileStatus.Failed && metadata.ReceiptCalls == 0);
        });
        foreach (bool committed in new[] { false, true })
            await Test("Receipt failure preserves content, committed=" + committed, async () =>
            {
                var f = new RecoveryFixture(); var cause = new IOException("private/path credential");
                f.Metadata.ReceiptFailure = cause; f.Metadata.CommitReceiptBeforeFailure = committed;
                var error = await Throws<ArkyvaMetadataException>(() => f.Service.UploadAsync(new MemoryStream(new byte[] { 1 }), "x"));
                var row = f.Metadata.Rows.Values.Single(); row.CreatedUtc = DateTime.UtcNow.AddDays(-2);
                Check(error.InnerException == cause && row.Status == FileStatus.Pending && f.Storage.Deletes == 0);
                var result = await f.Recovery.RecoverAsync(Options());
                Check(result.RecordsRecovered == (committed ? 1 : 0) && f.Storage.Files.Count == 1);
            });
        await Test("Activation interruption leaves a verifiable receipt", async () =>
        {
            var f = new RecoveryFixture(); f.Metadata.UpdateFailure = new IOException("activation"); f.Metadata.FailureStatus = FileStatus.Active;
            await Throws<ArkyvaMetadataException>(() => f.Service.UploadAsync(new MemoryStream(new byte[] { 4 }), "x"));
            var row = f.Metadata.Rows.Values.Single(); row.CreatedUtc = DateTime.UtcNow.AddDays(-2);
            var result = await f.Recovery.RecoverAsync(Options());
            Check(result.RecordsRecovered == 1 && row.Status == FileStatus.Active && f.Storage.Deletes == 0);
        });
        foreach (FileStatus status in new[] { FileStatus.Pending, FileStatus.Failed })
            await Test("Verified receipt recovers " + status + " and repeat is safe", async () =>
            {
                var f = new RecoveryFixture(); var row = f.Seed(status);
                var result = await f.Recovery.RecoverAsync(Options());
                Check(result.RecordsRecovered == 1 && f.Metadata.Rows[row.FileId].Status == FileStatus.Active);
                Check((await f.Recovery.RecoverAsync(Options())).RecordsRecovered == 0 && f.Storage.Deletes == 0);
            });
        foreach (bool exists in new[] { false, true })
            await Test("Legacy Pending without receipt retained, content=" + exists, async () =>
            {
                var f = new RecoveryFixture(); var row = f.Seed(FileStatus.Pending, exists, false);
                var result = await f.Recovery.RecoverAsync(Options());
                Check(result.OperationsSkipped == 1 && result.RecordsRecovered == 0 && result.RecordsMarkedFailed == 0 && f.Metadata.Rows[row.FileId].Status == FileStatus.Pending);
            });
        await Test("Failed legacy content cannot be activated just because it exists", async () =>
        {
            var f = new RecoveryFixture(); var row = f.Seed(FileStatus.Failed, true, false);
            var result = await f.Recovery.RecoverAsync(Options()); Check(result.OperationsSkipped == 1 && f.Metadata.Rows[row.FileId].Status == FileStatus.Failed);
        });
        foreach (string fault in new[] { "missing", "length", "hash" })
            await Test("Pending receipt mismatch becomes Failed: " + fault, async () =>
            {
                var f = new RecoveryFixture(); var row = f.Seed(FileStatus.Pending, fault != "missing");
                if (fault == "length") f.Storage.Files[row.StorageKey] = new byte[] { 1 };
                if (fault == "hash") f.Storage.Files[row.StorageKey] = new byte[] { 9, 9, 9 };
                var result = await f.Recovery.RecoverAsync(Options());
                Check(result.RecordsMarkedFailed == 1 && f.Metadata.Rows[row.FileId].Status == FileStatus.Failed && f.Storage.Deletes == 0);
                Check((await f.Recovery.RecoverAsync(Options())).RecordsMarkedFailed == 0);
            });
        await Test("Active missing and Deleted content are diagnosis only", async () =>
        {
            var f = new RecoveryFixture(); var active = f.Seed(FileStatus.Active, false); var deleted = f.Seed(FileStatus.Deleted);
            var result = await f.Recovery.RecoverAsync(Options());
            Check(result.Findings.Any(x => x.Code == "ActiveContentMissing") && result.Findings.Any(x => x.Code == "DeletedContentRetained"));
            Check(f.Metadata.Rows[active.FileId].Status == FileStatus.Active && f.Metadata.Rows[deleted.FileId].Status == FileStatus.Deleted && f.Storage.Deletes == 0);
        });
        await Test("DryRun proposes recovery and failure without writes", async () =>
        {
            var f = new RecoveryFixture(); f.Seed(FileStatus.Pending); f.Seed(FileStatus.Pending, false);
            var result = await f.Recovery.RecoverAsync(Options(true));
            Check(result.ProposedRecoveries == 1 && result.ProposedFailures == 1 && result.RecordsRecovered == 0 && result.RecordsMarkedFailed == 0);
            Check(f.Metadata.Updates == 0 && f.Metadata.RecoveryCalls == 0 && f.Metadata.Rows.Values.All(x => x.Status == FileStatus.Pending));
        });
        await Test("Young operations are excluded", async () =>
        {
            var f = new RecoveryFixture(); var row = f.Seed(FileStatus.Pending); f.Metadata.Rows[row.FileId].CreatedUtc = DateTime.UtcNow;
            Check((await f.Recovery.RecoverAsync(Options())).RecordsExamined == 0);
        });
        await Test("Two simultaneous recoveries produce one revision winner", async () =>
        {
            var f = new RecoveryFixture(); var row = f.Seed(FileStatus.Pending); int waiting = 0;
            var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Metadata.BeforeActivate = async () => { if (Interlocked.Increment(ref waiting) == 2) barrier.TrySetResult(true); await barrier.Task; };
            Task<RecoveryResult[]> task = Task.WhenAll(f.Recovery.RecoverAsync(Options()), f.Recovery.RecoverAsync(Options()));
            await Timeout(task); var results = await task;
            Check(results.Sum(x => x.RecordsRecovered) == 1 && results.Sum(x => x.OperationsSkipped) == 1 && f.Metadata.Rows[row.FileId].Revision == 1);
        });
        await Test("Delete wins against a stale recovery snapshot", async () =>
        {
            var f = new RecoveryFixture(); var row = f.Seed(FileStatus.Pending);
            f.Metadata.BeforeActivate = () => f.Service.DeleteAsync(row.FileId);
            var result = await f.Recovery.RecoverAsync(Options());
            Check(result.RecordsRecovered == 0 && result.OperationsSkipped == 1 && f.Metadata.Rows[row.FileId].Status == FileStatus.Deleted && f.Storage.Deletes == 0);
        });
        await Test("Two stale ordinary updates cannot overwrite each other", async () =>
        {
            var f = new RecoveryFixture(); var row = f.Seed(FileStatus.Active);
            var a = await f.Metadata.GetAsync(row.FileId); var b = await f.Metadata.GetAsync(row.FileId);
            a.Status = FileStatus.Failed; await f.Metadata.UpdateAsync(a);
            b.Status = FileStatus.Deleted; b.DeletedUtc = DateTime.UtcNow;
            await Throws<InvalidOperationException>(() => f.Metadata.UpdateAsync(b));
            Check(f.Metadata.Rows[row.FileId].Status == FileStatus.Failed);
        });
        await Test("Long-running upload is never failed based on age alone", async () =>
        {
            var metadata = new ReceiptMetadata(); var storage = new GatedStorage();
            var service = new ArkyvaService(storage, metadata, "FileSystem");
            Task<Guid> upload = service.UploadAsync(new MemoryStream(new byte[] { 1 }), "long.bin");
            await Timeout(storage.Entered.Task);
            try
            {
                var pending = metadata.Rows.Values.Single();
                // Ensure the real clock has advanced past the captured creation instant.
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow.Ticks <= pending.CreatedUtc.Ticks + 1)
                { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(1); }
                var options = Options(); options.MinimumAge = TimeSpan.FromTicks(1);
                var result = await new StorageRecoveryService(storage, metadata, "FileSystem").RecoverAsync(options);
                Check(result.OperationsSkipped == 1 && pending.Status == FileStatus.Pending);
            }
            finally { storage.Release.TrySetResult(true); }
            await upload;
        });
        await Test("Paged state changes do not skip records", async () =>
        {
            var f = new RecoveryFixture(); for (int i = 0; i < 7; i++) f.Seed(FileStatus.Pending);
            var options = Options(); options.PageSize = 2;
            var result = await f.Recovery.RecoverAsync(options); Check(result.RecordsExamined == 7 && result.RecordsRecovered == 7);
        });
        await Test("Configured bounds stop scanning", async () =>
        {
            var f = new RecoveryFixture(); for (int i = 0; i < 5; i++) f.Seed(FileStatus.Pending);
            var options = Options(); options.MaximumRecords = 2; options.PageSize = 1;
            var result = await f.Recovery.RecoverAsync(options); Check(result.RecordsExamined == 2 && result.Truncated && result.RecordsRecovered == 2);
        });
        await Test("Provider mismatches and I/O errors are safe diagnostics", async () =>
        {
            var f = new RecoveryFixture(); var row = f.Seed(FileStatus.Pending); f.Metadata.Rows[row.FileId].ProviderId = "other";
            var mismatch = await f.Recovery.RecoverAsync(Options()); Check(mismatch.OperationsSkipped == 1 && f.Storage.Reads == 0);
            f.Metadata.Rows[row.FileId].ProviderId = "FileSystem"; f.Storage.ReadFailure = new IOException("private/path password");
            var result = await f.Recovery.RecoverAsync(Options());
            Check(result.Errors == 1 && result.Findings.Single().ErrorType == "IOException" && result.RecordsMarkedFailed == 0);
        });
        await Test("Invalid maintenance settings fail before queries", async () =>
        {
            var f = new RecoveryFixture(); await Throws<ArgumentNullException>(() => f.Recovery.RecoverAsync(null));
            foreach (var option in new[] { new RecoveryOptions { MinimumAge = TimeSpan.Zero }, new RecoveryOptions { PageSize = 0 },
                new RecoveryOptions { MaximumRecords = 0 }, new RecoveryOptions { MaximumStorageEntries = 0 } })
                await Throws<ArgumentOutOfRangeException>(() => f.Recovery.RecoverAsync(option));
            Check(f.Metadata.Lists == 0);
        });
        await FilesystemDiagnostics();
    }
    private static async Task FilesystemDiagnostics()
    {
        string root = Path.Combine(Path.GetTempPath(), "Arkyva-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Test("Inventory retains old/recent/active temporaries and unreferenced definitive content", async () =>
            {
                var provider = new FileSystemStorageProvider(root); var metadata = new ReceiptMetadata();
                string old = Path.Combine(root, ".arkyva-" + Guid.NewGuid().ToString("N") + ".tmp");
                string recent = Path.Combine(root, ".arkyva-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(old, "abandoned-looking"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3)); File.WriteAllText(recent, "recent");
                using (var active = new FileStream(Path.Combine(root, ".arkyva-" + Guid.NewGuid().ToString("N") + ".tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await provider.SaveAsync("unknown.bin", new MemoryStream(new byte[] { 4 }));
                    var row = new FileMetadata { FileId = Guid.NewGuid(), OriginalName = "x", ProviderId = "FileSystem", StorageKey = "deleted.bin", CreatedUtc = DateTime.UtcNow.AddDays(-4), Status = FileStatus.Deleted, DeletedUtc = DateTime.UtcNow.AddDays(-1) };
                    metadata.Rows.Add(row.FileId, row); await provider.SaveAsync(row.StorageKey, new MemoryStream(new byte[] { 2 }));
                    var result = await new StorageRecoveryService(provider, metadata, "FileSystem").RecoverAsync(new RecoveryOptions { DryRun = false, CleanTemporaries = true });
                    Check(result.TemporariesIdentified == 3 && result.TemporariesDeleted == 0 && File.Exists(old) && File.Exists(recent));
                    Check(result.Findings.Any(x => x.Code == "OldTemporaryUncertain") && result.Findings.Any(x => x.Code == "RecentTemporaryRetained"));
                    Check(result.Findings.Any(x => x.Code == "UnreferencedContentUncertain") && result.Findings.Any(x => x.Code == "DeletedContentRetained"));
                    Check(await provider.ExistsAsync("unknown.bin") && await provider.ExistsAsync("deleted.bin"));
                    Check(result.Findings.Where(x => x.Reference != null).All(x => x.Reference.Length == 64 && !x.Reference.Contains("bin")));
                    var inventory = await provider.InspectAsync(1); Check(inventory.Truncated && inventory.Entries.Count <= 1);
                }
            });
            await Test("Real FileSystem publication can be recovered with a verified receipt", async () =>
            {
                var provider = new FileSystemStorageProvider(Path.Combine(root, "verified"));
                var metadata = new ReceiptMetadata { UpdateFailure = new IOException("Simulated interruption"), FailureStatus = FileStatus.Active };
                var service = new ArkyvaService(provider, metadata, "FileSystem");
                await Throws<ArkyvaMetadataException>(() => service.UploadAsync(new MemoryStream(new byte[] { 1, 2, 3, 4 }), "real.bin"));
                var row = metadata.Rows.Values.Single(); row.CreatedUtc = DateTime.UtcNow.AddDays(-2);
                var result = await new StorageRecoveryService(provider, metadata, "FileSystem").RecoverAsync(Options());
                Check(result.RecordsRecovered == 1 && await provider.ExistsAsync(row.StorageKey));
                using (var content = await service.DownloadAsync(row.FileId))
                using (var copy = new MemoryStream()) { await content.Content.CopyToAsync(copy); Check(copy.ToArray().SequenceEqual(new byte[] { 1, 2, 3, 4 })); }
            });
        }
        finally
        {
            string full = Path.GetFullPath(root);
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Arkyva-recovery-", StringComparison.Ordinal)) throw new Exception("Unsafe cleanup target");
            Directory.Delete(full, true);
        }
    }
    private static async Task Timeout(Task task) { if (await Task.WhenAny(task, Task.Delay(10000)) != task) throw new TimeoutException(); await task; }
    private static void Check(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Test(string name, Func<Task> action)
    { try { await action(); passed++; Console.WriteLine("PASS: " + name); } catch { Console.Error.WriteLine("FAIL: " + name); throw; } }
}
