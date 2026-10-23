using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Arkyva.Core.Exceptions;
using Arkyva.Core.Models;
using Arkyva.Core.Services;
using PublicInfo = Arkyva.Core.Models.FileInfo;

internal static class Program
{
    private static int tests;
    private static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine(tests + " service tests passed (fakes; no SQL or SMB)."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task Run()
    {
        await Test("Upload registers Pending before storage and activates with exact bytes", async () =>
        {
            var f = new Fixture();
            f.Storage.OnSave = () => Check(f.Metadata.Rows.Values.Single().Status == FileStatus.Pending && f.Metadata.Rows.Values.Single().SizeBytes == 0);
            using (var input = new TrackingStream(new byte[] { 1, 2, 3, 4, 5 }))
            {
                input.Position = 2;
                Guid id = await f.Service.UploadAsync(input, "contrato.pdf");
                var row = f.Metadata.Rows[id];
                Check(!input.Disposed && id != Guid.Empty && row.Status == FileStatus.Active && row.SizeBytes == 3);
                Check(row.Extension == ".pdf" && row.OriginalName == "contrato.pdf" && row.MimeType == "application/octet-stream");
                Check(row.CreatedUtc.Kind == DateTimeKind.Utc && row.ProviderId == "FileSystem" && row.DeletedUtc == null);
                Check(Regex.IsMatch(row.StorageKey, @"\A[0-9]{4}/[0-9]{2}/[0-9a-f]{32}\.bin\z") && !row.StorageKey.Contains("contrato"));
                Check(f.Storage.LastKey == row.StorageKey && f.Metadata.Writes.Select(x => x.StorageKey).Distinct().Count() == 1);
                Check(f.Metadata.Writes.Select(x => x.Status).SequenceEqual(new[] { FileStatus.Pending, FileStatus.Active }));
                Check(f.Storage.Files[row.StorageKey].SequenceEqual(new byte[] { 3, 4, 5 }));
            }
        });
        await Test("Upload IDs and keys unique under concurrent calls", async () =>
        {
            var f = new Fixture();
            Guid[] ids = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() => f.Service.UploadAsync(new MemoryStream(new byte[] { 1 }), "same.pdf"))));
            Check(ids.Distinct().Count() == 16 && f.Metadata.Rows.Values.Select(x => x.StorageKey).Distinct().Count() == 16);
        });
        await Test("Non-seekable large stream transfers with bounded buffers", async () =>
        {
            var f = new Fixture(); f.Storage.Discard = true;
            using (var input = new GeneratedStream(32L * 1024 * 1024 + 13))
            {
                Guid id = await f.Service.UploadAsync(input, "large");
                Check(f.Metadata.Rows[id].SizeBytes == 32L * 1024 * 1024 + 13 && f.Metadata.Rows[id].Extension == "" && !input.Disposed);
            }
        });
        await Test("Empty input activates at zero bytes", async () =>
        {
            var f = new Fixture(); Guid id = await f.Service.UploadAsync(new MemoryStream(), ".gitignore");
            Check(f.Metadata.Rows[id].SizeBytes == 0 && f.Metadata.Rows[id].Status == FileStatus.Active);
        });
        await Test("Disposing provider wrapper does not close caller stream", async () =>
        {
            var f = new Fixture(); f.Storage.DisposeInput = true;
            using (var input = new TrackingStream(new byte[] { 1 })) { await f.Service.UploadAsync(input, "x"); Check(!input.Disposed); }
        });
        foreach (bool committed in new[] { false, true })
            await Test("Registration failure prevents storage, uncertain commit=" + committed, async () =>
            {
                var f = new Fixture(); var cause = new IOException("SQL private connection");
                f.Metadata.AddFailure = cause; f.Metadata.CommitAddBeforeFailure = committed;
                var error = await Throws<ArkyvaMetadataException>(() => f.Service.UploadAsync(new MemoryStream(), "a"));
                Check(error.InnerException == cause && error.FileId.HasValue && f.Storage.Saves == 0 && f.Metadata.Updates == 0 && !error.Message.Contains("private"));
                Check(f.Metadata.Rows.Count == (committed ? 1 : 0));
            });
        foreach (bool published in new[] { false, true })
            await Test("Storage failure marks Failed without deleting, uncertain publish=" + published, async () =>
            {
                var f = new Fixture(); var cause = new IOException("storage private/path");
                f.Storage.SaveFailure = cause; f.Storage.PublishBeforeFailure = published;
                using (var input = new TrackingStream(new byte[] { 1 }))
                {
                    var error = await Throws<ArkyvaStorageException>(() => f.Service.UploadAsync(input, "a"));
                    Check(error.InnerException == cause && error.FileId == f.Metadata.Rows.Keys.Single() && !input.Disposed && !error.Message.Contains("private"));
                }
                Check(f.Metadata.Rows.Values.Single().Status == FileStatus.Failed && f.Storage.Deletes == 0 && f.Storage.Saves == 1);
                Check(f.Storage.Files.Count == (published ? 1 : 0));
            });
        await Test("Input read failure cannot activate a record", async () =>
        {
            var f = new Fixture(); using (var input = new GeneratedStream(4, true))
            {
                await Throws<ArkyvaStorageException>(() => f.Service.UploadAsync(input, "bad"));
                Check(!input.Disposed && f.Metadata.Rows.Values.Single().Status == FileStatus.Failed);
            }
        });
        await Test("Failed-state persistence preserves both causes and Pending record", async () =>
        {
            var f = new Fixture(); f.Storage.SaveFailure = new IOException("physical"); f.Metadata.UpdateFailure = new IOException("metadata");
            var error = await Throws<ArkyvaStorageException>(() => f.Service.UploadAsync(new MemoryStream(), "bad"));
            var causes = error.InnerException as AggregateException;
            Check(causes != null && causes.InnerExceptions[0] == f.Storage.SaveFailure && causes.InnerExceptions[1] == f.Metadata.UpdateFailure);
            Check(f.Metadata.Rows.Values.Single().Status == FileStatus.Pending && f.Storage.Deletes == 0);
        });
        foreach (bool committed in new[] { false, true })
            await Test("Activation failure retains complete content, uncertain commit=" + committed, async () =>
            {
                var f = new Fixture(); var cause = new IOException("SQL activation"); f.Metadata.UpdateFailure = cause;
                f.Metadata.FailureStatus = FileStatus.Active; f.Metadata.CommitUpdateBeforeFailure = committed;
                var error = await Throws<ArkyvaMetadataException>(() => f.Service.UploadAsync(new MemoryStream(new byte[] { 7, 8 }), "file"));
                Check(error.InnerException == cause && f.Storage.Deletes == 0 && f.Storage.Files.Values.Single().Length == 2 && f.Metadata.Updates == 1);
                Check(f.Metadata.Rows.Values.Single().Status == (committed ? FileStatus.Active : FileStatus.Pending));
            });
        await Test("Upload validates before creating any metadata", async () =>
        {
            var f = new Fixture();
            foreach (string name in new[] { null, "", " ", "a/b", "a\\b", "../file", "file.", "file ", "a:b", "a\n", new string('a', 256), "x." + new string('e', 31) })
                await Throws<ArgumentException>(() => f.Service.UploadAsync(new MemoryStream(), name));
            await Throws<ArgumentNullException>(() => f.Service.UploadAsync(null, "valid"));
            var disposed = new MemoryStream(); disposed.Dispose();
            await Throws<ArgumentException>(() => f.Service.UploadAsync(disposed, "valid"));
            Check(f.Metadata.Adds == 0 && f.Storage.Saves == 0);
        });
        await Test("Constructor validates dependencies and provider identity", async () =>
        {
            var f = new Fixture();
            await Throws<ArgumentNullException>(() => Task.FromResult(new ArkyvaService(null, f.Metadata, "p")));
            await Throws<ArgumentNullException>(() => Task.FromResult(new ArkyvaService(f.Storage, null, "p")));
            foreach (string id in new[] { null, " ", new string('x', 101) })
                await Throws<ArgumentException>(() => Task.FromResult(new ArkyvaService(f.Storage, f.Metadata, id)));
        });
        await Test("Download contains public metadata and owns its stream", async () =>
        {
            var f = new Fixture(); var row = f.Seed();
            using (FileContent result = await f.Service.DownloadAsync(row.FileId))
            using (var copy = new MemoryStream())
            {
                Check(result.Info.GetType() == typeof(PublicInfo) && result.Info.OriginalName == row.OriginalName && result.Info.MimeType == row.MimeType && result.Info.SizeBytes == 3);
                await result.Content.CopyToAsync(copy); Check(copy.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }));
            }
            Check(f.Storage.LastRead.Disposed);
        });
        await Test("Download hides concrete provider stream and sanitizes consumption errors", async () =>
        {
            var f = new Fixture(); var row = f.Seed(); f.Storage.ReturnedStream = new GeneratedStream(3, true);
            using (var result = await f.Service.DownloadAsync(row.FileId))
            {
                Check(!ReferenceEquals(result.Content, f.Storage.ReturnedStream));
                var error = await Throws<ArkyvaStorageException>(() => result.Content.CopyToAsync(Stream.Null));
                Check(error.InnerException is IOException && error.FileId == row.FileId && !error.Message.Contains("private"));
            }
            Check(((GeneratedStream)f.Storage.ReturnedStream).Disposed);
        });
        await Test("Download missing record is specific not found", async () =>
        {
            var f = new Fixture(); var error = await Throws<ArkyvaFileNotFoundException>(() => f.Service.DownloadAsync(Guid.NewGuid())); Check(f.Storage.Reads == 0 && error.FileName == null);
        });
        foreach (FileStatus status in new[] { FileStatus.Pending, FileStatus.Deleted, FileStatus.Failed })
            await Test("Download refuses state " + status, async () =>
            {
                var f = new Fixture(); var row = f.Seed(status);
                var error = await Throws<ArkyvaFileUnavailableException>(() => f.Service.DownloadAsync(row.FileId));
                Check(error.FileId == row.FileId && error.Status == status && f.Storage.Reads == 0);
            });
        await Test("Download physical absence is safe with original cause", async () =>
        {
            var f = new Fixture(); var row = f.Seed(); f.Storage.Files.Clear();
            var error = await Throws<ArkyvaFileNotFoundException>(() => f.Service.DownloadAsync(row.FileId));
            Check(error.InnerException is FileNotFoundException && !error.Message.Contains("private") && error.FileName == null);
        });
        await Test("Download access error keeps physical cause", async () =>
        {
            var f = new Fixture(); var row = f.Seed(); f.Storage.ReadFailure = new UnauthorizedAccessException("private/path");
            var error = await Throws<ArkyvaStorageException>(() => f.Service.DownloadAsync(row.FileId));
            Check(error.InnerException == f.Storage.ReadFailure && !error.Message.Contains("private"));
        });
        await Test("Wrong provider cannot read content or report availability", async () =>
        {
            var f = new Fixture(); var row = f.Seed(); f.Metadata.Rows[row.FileId].ProviderId = "different";
            await Throws<ArkyvaFileUnavailableException>(() => f.Service.DownloadAsync(row.FileId));
            Check(!await f.Service.ExistsAsync(row.FileId) && f.Storage.Reads == 0 && f.Storage.Exists == 0);
        });
        await Test("FileContent strips a technical DTO supplied directly", () =>
        {
            var f = new Fixture(); var row = f.Seed();
            using (var content = new FileContent(row, new MemoryStream())) Check(content.Info.GetType() == typeof(PublicInfo));
            return Task.CompletedTask;
        });
        await Test("GetInfo creates an independent public snapshot without storage access", async () =>
        {
            var f = new Fixture(); var row = f.Seed(); var info = await f.Service.GetInfoAsync(row.FileId);
            Check(info.GetType() == typeof(PublicInfo) && info.FileId == row.FileId && info.OriginalName == row.OriginalName);
            info.OriginalName = "changed";
            Check(f.Metadata.Rows[row.FileId].OriginalName == row.OriginalName && f.Storage.Reads == 0 && f.Storage.Exists == 0);
        });
        await Test("GetInfo returns null for missing or inactive records", async () =>
        {
            var f = new Fixture(); Check(await f.Service.GetInfoAsync(Guid.NewGuid()) == null);
            foreach (var status in new[] { FileStatus.Pending, FileStatus.Deleted, FileStatus.Failed }) Check(await f.Service.GetInfoAsync(f.Seed(status).FileId) == null);
        });
        await Test("List preserves pagination and maps DTOs without touching content", async () =>
        {
            var f = new Fixture(); for (int i = 0; i < 5; i++) f.Seed(); f.Seed(FileStatus.Deleted);
            var query = new FileQuery { PageNumber = 2, PageSize = 2 };
            var page = await f.Service.ListAsync(query);
            Check(page.TotalCount == 5 && page.Items.Count == 2 && page.Items.All(x => x.GetType() == typeof(PublicInfo) && x.Status == FileStatus.Active));
            Check(!ReferenceEquals(query, f.Metadata.LastQuery) && f.Metadata.LastQuery.PageNumber == 2 && f.Storage.Reads == 0 && f.Storage.Exists == 0);
            Check((await f.Service.ListAsync(new FileQuery { PageNumber = 9 })).Items.Count == 0);
        });
        await Test("List supports every Phase 3 filter and explicit nonactive states", async () =>
        {
            var f = new Fixture(); var row = f.Seed(FileStatus.Deleted, "Exact.pdf"); f.Seed();
            var query = new FileQuery { FileId = row.FileId, OriginalName = row.OriginalName, Extension = ".pdf", Status = FileStatus.Deleted,
                CreatedFromUtc = row.CreatedUtc, CreatedBeforeUtc = row.CreatedUtc.AddSeconds(1) };
            var page = await f.Service.ListAsync(query);
            Check(page.TotalCount == 1 && page.Items.Single().FileId == row.FileId && f.Metadata.LastQuery.CreatedFromUtc == row.CreatedUtc);
            Check((await f.Service.ListAsync(new FileQuery { Status = null })).TotalCount == 2);
            query.OriginalName = "exact.pdf"; Check((await f.Service.ListAsync(query)).TotalCount == 0);
        });
        await Test("Public DTOs expose no technical properties", () =>
        {
            Check(!typeof(PublicInfo).GetProperties().Any(p => p.Name == "ProviderId" || p.Name == "StorageKey" || p.Name.Contains("Path") || p.Name.Contains("Connection")));
            Check(typeof(FileContent).GetProperties().All(p => p.Name == "Info" || p.Name == "Content"));
            return Task.CompletedTask;
        });
        await Test("Invalid IDs and query filters fail before dependencies", async () =>
        {
            var f = new Fixture();
            await Throws<ArgumentException>(() => f.Service.DownloadAsync(Guid.Empty)); await Throws<ArgumentException>(() => f.Service.GetInfoAsync(Guid.Empty));
            await Throws<ArgumentException>(() => f.Service.ExistsAsync(Guid.Empty)); await Throws<ArgumentException>(() => f.Service.DeleteAsync(Guid.Empty));
            await Throws<ArgumentNullException>(() => f.Service.ListAsync(null));
            foreach (var query in new[] { new FileQuery { PageNumber = 0 }, new FileQuery { PageSize = 1001 }, new FileQuery { PageSize = 0 },
                new FileQuery { FileId = Guid.Empty }, new FileQuery { Status = (FileStatus)99 }, new FileQuery { OriginalName = new string('x', 256) },
                new FileQuery { Extension = new string('x', 31) }, new FileQuery { CreatedFromUtc = DateTime.Now },
                new FileQuery { CreatedBeforeUtc = DateTime.Now }, new FileQuery { CreatedFromUtc = DateTime.UtcNow.AddDays(1), CreatedBeforeUtc = DateTime.UtcNow } })
                await Throws<ArgumentException>(() => f.Service.ListAsync(query));
            Check(f.Metadata.Gets == 0 && f.Metadata.Lists == 0);
        });
        await Test("Exists requires Active record and physical content", async () =>
        {
            var f = new Fixture(); Check(!await f.Service.ExistsAsync(Guid.NewGuid()));
            foreach (var status in new[] { FileStatus.Pending, FileStatus.Deleted, FileStatus.Failed }) Check(!await f.Service.ExistsAsync(f.Seed(status).FileId));
            Check(f.Storage.Exists == 0);
            var active = f.Seed(); Check(await f.Service.ExistsAsync(active.FileId));
            f.Storage.Files.Clear(); Check(!await f.Service.ExistsAsync(active.FileId));
        });
        await Test("Exists propagates physical failure safely", async () =>
        {
            var f = new Fixture(); var row = f.Seed(); f.Storage.ExistsFailure = new IOException("private/path");
            var error = await Throws<ArkyvaStorageException>(() => f.Service.ExistsAsync(row.FileId)); Check(error.InnerException == f.Storage.ExistsFailure);
        });
        foreach (FileStatus state in new[] { FileStatus.Active, FileStatus.Pending, FileStatus.Failed })
            await Test("Logical deletion from " + state + " is UTC and idempotent", async () =>
            {
                var f = new Fixture(); var row = f.Seed(state); DateTime before = DateTime.UtcNow;
                await f.Service.DeleteAsync(row.FileId); var deleted = f.Metadata.Rows[row.FileId];
                Check(deleted.Status == FileStatus.Deleted && deleted.DeletedUtc.Value.Kind == DateTimeKind.Utc && deleted.DeletedUtc >= before);
                await f.Service.DeleteAsync(row.FileId);
                Check(f.Metadata.Updates == 1 && f.Metadata.Rows[row.FileId].DeletedUtc == deleted.DeletedUtc && f.Storage.Deletes == 0 && f.Storage.Files.ContainsKey(row.StorageKey));
                await Throws<ArkyvaFileUnavailableException>(() => f.Service.DownloadAsync(row.FileId));
                Check((await f.Service.ListAsync(new FileQuery())).TotalCount == 0);
            });
        await Test("Missing deletion is idempotent", async () =>
        {
            var f = new Fixture(); await f.Service.DeleteAsync(Guid.NewGuid()); Check(f.Metadata.Updates == 0 && f.Storage.Deletes == 0);
        });
        await Test("Concurrent logical deletions confirm the persisted result", async () =>
        {
            var f = new Fixture(); var row = f.Seed(); int reads = 0;
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Metadata.OnGet = async () =>
            {
                int call = Interlocked.Increment(ref reads);
                if (call <= 2) { if (call == 2) ready.TrySetResult(true); await ready.Task; }
            };
            Task deletes = Task.WhenAll(f.Service.DeleteAsync(row.FileId), f.Service.DeleteAsync(row.FileId));
            if (await Task.WhenAny(deletes, Task.Delay(10000)) != deletes) throw new TimeoutException();
            await deletes; Check(f.Metadata.Rows[row.FileId].Status == FileStatus.Deleted && f.Storage.Deletes == 0);
        });
        foreach (bool committed in new[] { false, true })
            await Test("Deletion failure confirms uncertain result, committed=" + committed, async () =>
            {
                var f = new Fixture(); var row = f.Seed(); f.Metadata.UpdateFailure = new IOException("metadata"); f.Metadata.CommitUpdateBeforeFailure = committed;
                if (committed) await f.Service.DeleteAsync(row.FileId);
                else { var error = await Throws<ArkyvaMetadataException>(() => f.Service.DeleteAsync(row.FileId)); Check(error.InnerException == f.Metadata.UpdateFailure); }
                Check(f.Storage.Deletes == 0 && f.Metadata.Rows[row.FileId].Status == (committed ? FileStatus.Deleted : FileStatus.Active));
            });
        await Test("Metadata read/list errors keep causes without exposing messages", async () =>
        {
            var f = new Fixture(); var cause = new IOException("private connection credentials"); f.Metadata.GetFailure = cause; f.Metadata.ListFailure = cause;
            Func<Task>[] calls = { () => f.Service.GetInfoAsync(Guid.NewGuid()), () => f.Service.DownloadAsync(Guid.NewGuid()),
                () => f.Service.ExistsAsync(Guid.NewGuid()), () => f.Service.DeleteAsync(Guid.NewGuid()), () => f.Service.ListAsync(new FileQuery()) };
            foreach (var call in calls) { var error = await Throws<ArkyvaMetadataException>(call); Check(error.InnerException == cause && !error.Message.Contains("private")); }
        });
    }
    private static async Task Test(string name, Func<Task> action)
    {
        try { await action(); tests++; Console.WriteLine("PASS: " + name); }
        catch { Console.Error.WriteLine("FAIL: " + name); throw; }
    }
    private static void Check(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
