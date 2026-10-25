using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arkyva;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using Arkyva.Core.Services;
using Arkyva.Core.Exceptions;

internal static class SynchronousServiceChecks
{
    private static int checks;
    private static readonly byte[] Payload = { 0, 1, 2, 127, 255 };
    internal static async Task<int> Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "Arkyva-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Test("Six native operations never call Async dependencies and keep stream ownership", () =>
            {
                var f = new Context(root);
                using (var input = new TrackingStream(Payload))
                {
                    Guid id = f.Service.Upload(input, "native.bin");
                    Require(!input.Disposed && f.Service.Exists(id));
                    var row = f.Data.Get(id);
                    Require(row.Status == FileStatus.Active && row.CompletionSha256.Length == 64 && row.SizeBytes == Payload.Length);
                    Require(f.Data.Writes.Select(m => m.Status).SequenceEqual(new[] { FileStatus.Pending, FileStatus.Pending, FileStatus.Active }));
                    using (var downloaded = f.Service.Download(id))
                    using (var copy = new MemoryStream())
                    {
                        downloaded.Content.CopyTo(copy);
                        Require(copy.ToArray().SequenceEqual(Payload) && !((FileStream)f.Storage.LastRead).IsAsync);
                    }
                    Require(!f.Storage.LastRead.CanRead);
                    Require(f.Service.GetInfo(id).GetType() == typeof(Arkyva.Core.Models.FileInfo));
                    Require(f.Service.List(new FileQuery { FileId = id, PageSize = 1 }).TotalCount == 1);
                    f.Service.Delete(id);
                    DateTime? deleted = f.Data.Get(id).DeletedUtc;
                    f.Service.Delete(id);
                    Require(deleted.HasValue && deleted.Value.Kind == DateTimeKind.Utc && f.Data.Get(id).DeletedUtc == deleted);
                    Require(!f.Service.Exists(id) && f.Service.GetInfo(id) == null && f.Service.List(new FileQuery()).TotalCount == 0);
                    Throws<ArkyvaFileUnavailableException>(() => f.Service.Download(id));
                    Require(f.Storage.Inner.Exists(row.StorageKey) && f.Storage.Deletes == 0);
                    Require(f.Storage.AsyncCalls == 0 && f.Data.AsyncCalls == 0);
                }
            });
            Test("Empty synchronous upload has zero size and a verifiable receipt", () =>
            {
                var f = new Context(root); Guid id;
                using (var input = new MemoryStream()) id = f.Service.Upload(input, "empty.bin");
                Require(f.Data.Get(id).SizeBytes == 0 && f.Data.Get(id).CompletionSha256.Length == 64);
            });
            Test("Invalid input, closed stream, names, IDs and pagination fail before writes", () =>
            {
                var f = new Context(root);
                Throws<ArgumentNullException>(() => f.Service.Upload(null, "x.bin"));
                var closed = new MemoryStream(); closed.Dispose();
                Throws<ArgumentException>(() => f.Service.Upload(closed, "x.bin"));
                using (var input = new MemoryStream(Payload))
                    foreach (string name in new[] { null, "", "../x.bin", " a.bin", "x.", new string('x', 256) })
                        Throws<ArgumentException>(() => f.Service.Upload(input, name));
                Throws<ArgumentException>(() => f.Service.Download(Guid.Empty));
                Throws<ArgumentException>(() => f.Service.GetInfo(Guid.Empty));
                Throws<ArgumentException>(() => f.Service.Exists(Guid.Empty));
                Throws<ArgumentException>(() => f.Service.Delete(Guid.Empty));
                Throws<ArgumentNullException>(() => f.Service.List(null));
                Throws<ArgumentOutOfRangeException>(() => f.Service.List(new FileQuery { PageNumber = 0 }));
                Require(f.Data.Adds == 0 && f.Storage.Saves == 0);
            });
            Test("Synchronous missing content and nonactive states match asynchronous semantics", () =>
            {
                var f = new Context(root); Guid missing = Guid.NewGuid();
                Require(f.Service.GetInfo(missing) == null && !f.Service.Exists(missing));
                f.Service.Delete(missing);
                Throws<ArkyvaFileNotFoundException>(() => f.Service.Download(missing));
                Guid id = f.Upload(); var row = f.Data.Rows[id];
                foreach (var state in new[] { FileStatus.Pending, FileStatus.Failed, FileStatus.Deleted })
                {
                    row.Status = state;
                    Require(!f.Service.Exists(id) && f.Service.GetInfo(id) == null);
                    Throws<ArkyvaFileUnavailableException>(() => f.Service.Download(id));
                }
                row.Status = FileStatus.Active; f.Storage.Inner.Delete(row.StorageKey);
                Require(!f.Service.Exists(id));
                Throws<ArkyvaFileNotFoundException>(() => f.Service.Download(id));
            });
            Test("Large nonseekable native stream rejects all Async reads and leaves input open", () =>
            {
                var f = new Context(root);
                using (var input = new SyncOnlyStream(12L * 1024 * 1024))
                {
                    Guid id = f.Service.Upload(input, "large.bin");
                    Require(f.Service.GetInfo(id).SizeBytes == 12L * 1024 * 1024 && !input.Disposed);
                }
            });
            foreach (bool committed in new[] { false, true })
            {
                Test("Synchronous registration failure never writes content; uncertain=" + committed, () =>
                {
                    var f = new Context(root); var cause = new IOException("SQL private connection");
                    f.Data.AddFailure = cause; f.Data.CommitAddBeforeFailure = committed;
                    var error = Throws<ArkyvaMetadataException>(() => f.Upload());
                    Require(error.InnerException == cause && error.FileId.HasValue && f.Storage.Saves == 0);
                    Require(f.Data.Rows.Count == (committed ? 1 : 0));
                });
                Test("Synchronous physical failure preserves uncertain content; published=" + committed, () =>
                {
                    var f = new Context(root); var cause = new IOException("private disk path");
                    f.Storage.SaveFailure = cause; f.Storage.PublishBeforeFailure = committed;
                    var error = Throws<ArkyvaStorageException>(() => f.Upload());
                    var row = f.Data.Rows.Values.Single();
                    Require(error.InnerException == cause && row.Status == FileStatus.Failed && row.CompletionSha256 == null);
                    Require(f.Storage.Inner.Exists(row.StorageKey) == committed && f.Storage.Deletes == 0);
                });
                Test("Synchronous activation failure retains receipt and physical data; committed=" + committed, () =>
                {
                    var f = new Context(root); var cause = new IOException("activation error");
                    f.Data.UpdateFailure = cause; f.Data.FailureStatus = FileStatus.Active; f.Data.CommitUpdateBeforeFailure = committed;
                    Require(Throws<ArkyvaMetadataException>(() => f.Upload()).InnerException == cause);
                    var row = f.Data.Rows.Values.Single();
                    Require(row.Status == (committed ? FileStatus.Active : FileStatus.Pending) && row.CompletionSha256.Length == 64);
                    Require(f.Storage.Inner.Exists(row.StorageKey) && f.Storage.Deletes == 0);
                });
                Test("Synchronous receipt failure retains content without activating; committed=" + committed, () =>
                {
                    var f = new Context(root); var cause = new IOException("receipt error");
                    f.Data.ReceiptFailure = cause; f.Data.CommitReceiptBeforeFailure = committed;
                    Require(Throws<ArkyvaMetadataException>(() => f.Upload()).InnerException == cause);
                    var row = f.Data.Rows.Values.Single();
                    Require(row.Status == FileStatus.Pending && (row.CompletionSha256 != null) == committed);
                    Require(f.Storage.Inner.Exists(row.StorageKey) && f.Storage.Deletes == 0);
                });
                Test("Synchronous logical deletion confirms uncertain SQL result; committed=" + committed, () =>
                {
                    var f = new Context(root); Guid id = f.Upload();
                    f.Data.UpdateFailure = new IOException("delete error"); f.Data.FailureStatus = FileStatus.Deleted; f.Data.CommitUpdateBeforeFailure = committed;
                    if (committed) f.Service.Delete(id); else Throws<ArkyvaMetadataException>(() => f.Service.Delete(id));
                    Require(f.Storage.Deletes == 0 && f.Data.Rows[id].Status == (committed ? FileStatus.Deleted : FileStatus.Active));
                });
            }
            Test("Native write failure cleans temporary and preserves aggregate causes", () =>
            {
                var f = new Context(root);
                f.Data.UpdateFailure = new IOException("failed-state error"); f.Data.FailureStatus = FileStatus.Failed;
                using (var input = new GeneratedStream(1024, true))
                {
                    var error = Throws<ArkyvaStorageException>(() => f.Service.Upload(input, "failed.bin"));
                    Require(error.InnerException is AggregateException && f.Data.Rows.Values.Single().Status == FileStatus.Pending);
                }
                Require(!Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories).Any());
            });
            Test("A native provider returning before EOF never produces Active or a receipt", () =>
            {
                var f = new Context(root); f.Storage.Incomplete = true;
                Throws<ArkyvaStorageException>(() => f.Upload());
                var row = f.Data.Rows.Values.Single();
                Require(row.Status == FileStatus.Failed && row.CompletionSha256 == null && f.Storage.Inner.Exists(row.StorageKey));
            });
            Test("Synchronous read/list errors keep their original causes and hide sensitive messages", () =>
            {
                var f = new Context(root); Guid id = f.Upload(); var cause = new IOException("private password/path");
                f.Storage.ReadFailure = cause;
                var error = Throws<ArkyvaStorageException>(() => f.Service.Download(id));
                Require(error.InnerException == cause && !error.Message.Contains("private"));
                f.Data.GetFailure = cause;
                Require(Throws<ArkyvaMetadataException>(() => f.Service.GetInfo(id)).InnerException == cause);
                f.Data.ListFailure = cause;
                Require(Throws<ArkyvaMetadataException>(() => f.Service.List(new FileQuery())).InnerException == cause);
            });
            Test("Async-only providers remain compatible and native Upload rejects before Pending", () =>
            {
                var legacy = new Fixture();
                using (var input = new MemoryStream(Payload)) Throws<NotSupportedException>(() => legacy.Service.Upload(input, "x.bin"));
                Throws<NotSupportedException>(() => legacy.Service.List(new FileQuery()));
                Throws<NotSupportedException>(() => legacy.Service.GetInfo(Guid.NewGuid()));
                Require(legacy.Metadata.Adds == 0 && legacy.Storage.Saves == 0);
            });
            Test("An existing third-party asynchronous service needs no new interface members", () =>
            {
                IArkyvaService legacy = new AsyncOnlyService();
                Throws<NotSupportedException>(() => legacy.Upload(Stream.Null, "x.bin"));
                Throws<ArgumentNullException>(() => ArkyvaSynchronousExtensions.Upload(null, Stream.Null, "x.bin"));
            });
            Test("Async-only recovery capability is rejected before synchronous registration", () =>
            {
                var f = new Context(root); var incomplete = new AsyncRecoveryOnlyMetadata();
                var service = new ArkyvaService(f.Storage, incomplete, "FileSystem");
                using (var input = new MemoryStream(Payload)) Throws<NotSupportedException>(() => service.Upload(input, "x.bin"));
                Require(incomplete.Inner.Adds == 0 && f.Storage.Saves == 0);
            });
            var mixed = new Context(root); mixed.AllowAsync();
            Guid syncId = mixed.Upload();
            using (var file = await mixed.Service.DownloadAsync(syncId))
            using (var copy = new MemoryStream()) { await file.Content.CopyToAsync(copy); Require(copy.ToArray().SequenceEqual(Payload)); }
            Pass("Upload interoperates with DownloadAsync");
            Guid asyncId;
            using (var input = new MemoryStream(Payload)) asyncId = await mixed.Service.UploadAsync(input, "async.bin");
            using (var file = mixed.Service.Download(asyncId)) Require(file.Info.SizeBytes == Payload.Length);
            Require(mixed.Service.GetInfo(asyncId).FileId == (await mixed.Service.GetInfoAsync(asyncId)).FileId);
            Pass("UploadAsync interoperates with Download and both metadata APIs");
            var concurrent = new Context(root); concurrent.AllowAsync();
            var nativeConcurrent = new Context(root);
            Guid[] nativeIds = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => nativeConcurrent.Upload())));
            Require(nativeIds.Distinct().Count() == 8 && nativeConcurrent.Data.Rows.Values.All(m => m.Status == FileStatus.Active && m.CompletionSha256 != null));
            Pass("Concurrent synchronous uploads never call Async dependencies");
            var readMode = new Context(root); readMode.AllowAsync();
            using (var asyncOnly = new AsyncOnlyReadStream(1024 * 1024))
            {
                Guid guarded = await readMode.Service.UploadAsync(asyncOnly, "async-only.bin");
                Require(readMode.Service.GetInfo(guarded).SizeBytes == 1024 * 1024);
            }
            Pass("Async Upload retains asynchronous stream reads instead of using native Save");
            Guid[] ids = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => i % 2 == 0
                ? Task.Run(() => concurrent.Upload())
                : AsyncUpload(concurrent)));
            Require(ids.Distinct().Count() == 12 && concurrent.Data.Rows.Values.All(m => m.Status == FileStatus.Active && m.SizeBytes == Payload.Length && m.CompletionSha256 != null));
            Require(concurrent.Service.List(new FileQuery { PageSize = 3 }).Items.Count == 3 && concurrent.Service.List(new FileQuery { OriginalName = "native.bin" }).TotalCount == 6);
            Pass("Concurrent native/async uploads have unique IDs, receipts and stable filtered pagination");
            foreach (bool native in new[] { true, false })
            {
                var recovery = new Context(root); recovery.AllowAsync(); recovery.Data.UpdateFailure = new IOException("interruption"); recovery.Data.FailureStatus = FileStatus.Active;
                if (native) Throws<ArkyvaMetadataException>(() => recovery.Upload());
                else { try { await AsyncUpload(recovery); throw new Exception("Expected activation failure"); } catch (ArkyvaMetadataException) { } }
                recovery.Data.UpdateFailure = null;
                // Simulate elapsed time on persisted mock state instead of depending on clock resolution.
                recovery.Data.Rows.Values.Single().CreatedUtc = DateTime.UtcNow.AddHours(-3);
                var service = new StorageRecoveryService(recovery.Storage, recovery.Data, "FileSystem");
                var options = new RecoveryOptions { DryRun = false, InspectStorage = false };
                Require((await service.RecoverAsync(options)).RecordsRecovered == 1 && recovery.Data.Rows.Values.Single().Status == FileStatus.Active);
                Require((await service.RecoverAsync(options)).RecordsRecovered == 0 && recovery.Storage.Deletes == 0);
                Pass("Async recovery recognizes interruption after " + (native ? "Upload" : "UploadAsync"));
            }
        }
        finally
        {
            string full = Path.GetFullPath(root);
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Arkyva-sync-", StringComparison.Ordinal)) throw new Exception("Unsafe cleanup");
            Directory.Delete(full, true);
        }
        return checks;
    }
    private static async Task<Guid> AsyncUpload(Context context)
    { using (var input = new MemoryStream(Payload)) return await context.Service.UploadAsync(input, "async.bin"); }
    private sealed class Context
    {
        internal readonly string Root;
        internal readonly SynchronousMetadata Data = new SynchronousMetadata();
        internal readonly SynchronousStorage Storage;
        internal readonly IArkyvaService Service;
        internal Context(string root) { Root = Path.Combine(root, Guid.NewGuid().ToString("N")); Storage = new SynchronousStorage(Root); Service = new ArkyvaService(Storage, Data, "FileSystem"); }
        internal void AllowAsync() { Data.DenyAsync = false; Storage.DenyAsync = false; }
        internal Guid Upload() { using (var input = new MemoryStream(Payload)) return Service.Upload(input, "native.bin"); }
    }
    private sealed class SyncOnlyStream : GeneratedStream
    {
        internal SyncOnlyStream(long length) : base(length) { }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { throw new Exception("Native upload attempted ReadAsync"); }
    }
    private sealed class AsyncOnlyReadStream : GeneratedStream
    {
        internal AsyncOnlyReadStream(long length) : base(length) { }
        public override int Read(byte[] buffer, int offset, int count) { throw new Exception("Async upload attempted a synchronous input read"); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { return Task.FromResult(base.Read(buffer, offset, count)); }
    }
    private sealed class AsyncOnlyService : IArkyvaService
    {
        public Task<Guid> UploadAsync(Stream content, string name) { return Task.FromResult(Guid.Empty); }
        public Task<FileContent> DownloadAsync(Guid id) { return Task.FromResult<FileContent>(null); }
        public Task<Arkyva.Core.Models.FileInfo> GetInfoAsync(Guid id) { return Task.FromResult<Arkyva.Core.Models.FileInfo>(null); }
        public Task<PagedResult<Arkyva.Core.Models.FileInfo>> ListAsync(FileQuery query) { return Task.FromResult(new PagedResult<Arkyva.Core.Models.FileInfo>()); }
        public Task<bool> ExistsAsync(Guid id) { return Task.FromResult(false); }
        public Task DeleteAsync(Guid id) { return Task.CompletedTask; }
    }
    private static void Test(string name, Action test) { test(); Pass(name); }
    private static void Pass(string name) { checks++; Console.WriteLine("PASS: Sync: " + name); }
    private static void Require(bool condition) { if (!condition) throw new Exception("Synchronous API assertion failed"); }
    private static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
}
