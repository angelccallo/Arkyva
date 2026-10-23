using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Arkyva.Core.Models;
using Arkyva.Metadata.SqlServer.Repositories;
using Microsoft.Data.SqlClient;

internal static class Program
{
    private static int checks;
    private static readonly DateTime Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(1234567);

    private static int Main()
    {
        try { Run().GetAwaiter().GetResult(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + ": SQL tests failed; connection credentials are not logged."); Console.Error.WriteLine(error.StackTrace); return 1; }
    }

    private static async Task Run()
    {
        await Validation();
        Console.WriteLine("Offline validation checks passed: " + checks);
        string configured = Environment.GetEnvironmentVariable("ARKYVA_SQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Console.WriteLine("SKIP SQL integration: ARKYVA_SQL_TEST_CONNECTION is not configured. No SQL Server operations were executed.");
            return;
        }
        await Integration(configured);
        Console.WriteLine("SQL integration passed; total checks: " + checks);
    }

    private static FileMetadata Metadata(string name = "document.pdf", FileStatus status = FileStatus.Pending)
    {
        return new FileMetadata
        {
            FileId = Guid.NewGuid(), OriginalName = name, Extension = ".pdf", MimeType = "application/pdf",
            SizeBytes = 123, ProviderId = "FileSystem", StorageKey = "2026/" + Guid.NewGuid().ToString("N"),
            Status = status, CreatedUtc = Created,
            DeletedUtc = status == FileStatus.Deleted ? Created.AddHours(1) : (DateTime?)null
        };
    }

    private static async Task Validation()
    {
        foreach (string connection in new[] { null, "", " ", "InvalidKey=x", "Data Source=invalid", "Initial Catalog=test" })
            await Throws<ArgumentException>(() => Task.FromResult(new SqlServerMetadataRepository(connection)));
        var repository = new SqlServerMetadataRepository("Data Source=invalid;Initial Catalog=validation;Integrated Security=True;Connect Timeout=1");
        await Throws<ArgumentNullException>(() => repository.AddAsync(null));
        await Throws<ArgumentNullException>(() => repository.UpdateAsync(null));
        Action<FileMetadata>[] mutations = {
            m => m.FileId = Guid.Empty, m => m.OriginalName = null, m => m.OriginalName = " ",
            m => m.OriginalName = new string('x', 256), m => m.ProviderId = null,
            m => m.ProviderId = new string('x', 101), m => m.StorageKey = "",
            m => m.StorageKey = new string('x', 1001), m => m.Extension = new string('x', 31),
            m => m.MimeType = new string('x', 256), m => m.SizeBytes = -1,
            m => m.Status = (FileStatus)999, m => m.CreatedUtc = DateTime.SpecifyKind(Created, DateTimeKind.Local),
            m => m.CreatedUtc = DateTime.SpecifyKind(Created, DateTimeKind.Unspecified),
            m => m.Status = FileStatus.Deleted, m => m.DeletedUtc = Created.AddHours(1),
            m => { m.Status = FileStatus.Deleted; m.DeletedUtc = Created.AddTicks(-1); },
            m => { m.Status = FileStatus.Deleted; m.DeletedUtc = DateTime.SpecifyKind(Created, DateTimeKind.Unspecified); }
        };
        foreach (var mutate in mutations)
        {
            var invalid = Metadata(); mutate(invalid);
            await Throws<ArgumentException>(() => repository.AddAsync(invalid));
            await Throws<ArgumentException>(() => repository.UpdateAsync(invalid));
        }
        await Throws<ArgumentException>(() => repository.GetAsync(Guid.Empty));
        await Throws<ArgumentException>(() => repository.ExistsAsync(Guid.Empty));
        await Throws<ArgumentNullException>(() => repository.ListAsync(null));
        FileQuery[] invalidQueries = {
            new FileQuery { PageNumber = 0 }, new FileQuery { PageSize = 0 }, new FileQuery { PageSize = 1001 },
            new FileQuery { FileId = Guid.Empty }, new FileQuery { Status = (FileStatus)999 },
            new FileQuery { OriginalName = new string('x', 256) }, new FileQuery { Extension = new string('x', 31) },
            new FileQuery { CreatedFromUtc = DateTime.Now }, new FileQuery { CreatedBeforeUtc = DateTime.Now },
            new FileQuery { CreatedFromUtc = Created, CreatedBeforeUtc = Created }
        };
        foreach (FileQuery query in invalidQueries) await Throws<ArgumentException>(() => repository.ListAsync(query));
        Check(new FileQuery().Status == FileStatus.Active, "Default query preserves active-only semantics");
    }

    private static async Task Integration(string configured)
    {
        // Always create a fresh database, never use the configured Initial Catalog.
        var builder = new SqlConnectionStringBuilder(configured);
        if (!string.IsNullOrEmpty(builder.AttachDBFilename) || builder.UserInstance)
            throw new ArgumentException("Attached files and user instances are not supported by the test runner.");
        builder.InitialCatalog = "master";
        string database = "ArkyvaTests_" + Guid.NewGuid().ToString("N");
        if (!Regex.IsMatch(database, @"\AArkyvaTests_[0-9a-f]{32}\z")) throw new Exception("Unsafe database name");
        bool created = false;
        using (var admin = new SqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            try
            {
                await Execute(admin, "CREATE DATABASE [" + database + "];");
                created = true;
                builder.InitialCatalog = database;
                using (var databaseConnection = new SqlConnection(builder.ConnectionString))
                {
                    await databaseConnection.OpenAsync();
                    // Apply scripts twice to check additive/idempotent installation.
                    for (int pass = 0; pass < 2; pass++)
                        foreach (string script in Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts"), "V*.sql").OrderBy(x => x))
                            foreach (string batch in Regex.Split(File.ReadAllText(script), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                                if (!string.IsNullOrWhiteSpace(batch)) await Execute(databaseConnection, batch);
                    checks++; Console.WriteLine("PASS: Versioned scripts and repeated installation");
                    await RepositoryChecks(builder.ConnectionString, databaseConnection);
                }
            }
            finally
            {
                if (created)
                {
                    // The only destructive action targets the exact database created by this run.
                    using (var pooled = new SqlConnection(builder.ConnectionString)) SqlConnection.ClearPool(pooled);
                    await Execute(admin, "ALTER DATABASE [" + database + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + database + "];");
                }
            }
        }
    }

    private static async Task RepositoryChecks(string connectionString, SqlConnection sql)
    {
        var repository = new SqlServerMetadataRepository(connectionString);
        var file = Metadata();
        await repository.AddAsync(file);
        FileMetadata read = await repository.GetAsync(file.FileId);
        Check(read.FileId == file.FileId && read.StorageKey == file.StorageKey && read.ProviderId == file.ProviderId &&
            read.OriginalName == file.OriginalName && read.MimeType == file.MimeType && read.Extension == file.Extension &&
            read.SizeBytes == file.SizeBytes && read.CreatedUtc == file.CreatedUtc && read.CreatedUtc.Kind == DateTimeKind.Utc && read.DeletedUtc == null,
            "Insert and full metadata roundtrip including UTC precision");
        Check(await repository.GetAsync(Guid.NewGuid()) == null && !await repository.ExistsAsync(Guid.NewGuid()), "Missing queries");
        Check(!await repository.ExistsAsync(file.FileId), "Pending is not active existence");
        await Duplicate(() => repository.AddAsync(file));
        var duplicateKey = Metadata(); duplicateKey.StorageKey = file.StorageKey;
        await Duplicate(() => repository.AddAsync(duplicateKey));
        var longKey = Metadata(); longKey.StorageKey = new string('k', 1000); longKey.ProviderId = new string('p', 100);
        await repository.AddAsync(longKey);
        Check((await repository.GetAsync(longKey.FileId)).StorageKey.Length == 1000, "Maximum provider/key lengths supported by unique hash index");

        file.Status = FileStatus.Active;
        file.SizeBytes = 321;
        await repository.UpdateAsync(file);
        Check(await repository.ExistsAsync(file.FileId) && (await repository.GetAsync(file.FileId)).SizeBytes == 321,
            "Pending to Active finalizes the measured size");
        file.SizeBytes = 999;
        await Throws<InvalidOperationException>(() => repository.UpdateAsync(file));
        file.SizeBytes = 321;
        file.OriginalName = "renamed.pdf";
        await repository.UpdateAsync(file);
        Check((await repository.GetAsync(file.FileId)).OriginalName == file.OriginalName, "Descriptive metadata update");
        file.StorageKey += "changed";
        await Throws<InvalidOperationException>(() => repository.UpdateAsync(file));
        file.StorageKey = read.StorageKey;
        file.Status = FileStatus.Pending;
        await Throws<InvalidOperationException>(() => repository.UpdateAsync(file));
        file.Status = FileStatus.Deleted; file.DeletedUtc = Created.AddHours(2);
        await repository.UpdateAsync(file);
        await repository.UpdateAsync(file);
        Check(!await repository.ExistsAsync(file.FileId) && (await repository.GetAsync(file.FileId)).DeletedUtc == file.DeletedUtc, "Logical deletion and same-state idempotence");
        file.Status = FileStatus.Active; file.DeletedUtc = null;
        await Throws<InvalidOperationException>(() => repository.UpdateAsync(file));
        await Throws<InvalidOperationException>(() => repository.UpdateAsync(Metadata()));
        var failed = Metadata(); await repository.AddAsync(failed);
        failed.Status = FileStatus.Failed; await repository.UpdateAsync(failed);
        failed.Status = FileStatus.Pending;
        await Throws<InvalidOperationException>(() => repository.UpdateAsync(failed));
        failed.Status = FileStatus.Deleted; failed.DeletedUtc = Created.AddHours(1);
        await repository.UpdateAsync(failed);
        Check((await repository.GetAsync(failed.FileId)).Status == FileStatus.Deleted, "Failed can be deleted but cannot be reset by stale updates");

        var optional = Metadata(); optional.Extension = null; optional.MimeType = null;
        await repository.AddAsync(optional);
        Check((await repository.GetAsync(optional.FileId)).Extension == null && (await repository.GetAsync(optional.FileId)).MimeType == null, "Nullable metadata");
        for (int i = 0; i < 5; i++)
        {
            var page = Metadata("page.pdf", FileStatus.Active); page.CreatedUtc = Created.AddMinutes(i);
            await repository.AddAsync(page);
        }
        var first = await repository.ListAsync(new FileQuery { OriginalName = "page.pdf", PageSize = 2 });
        var second = await repository.ListAsync(new FileQuery { OriginalName = "page.pdf", PageSize = 2, PageNumber = 2 });
        var third = await repository.ListAsync(new FileQuery { OriginalName = "page.pdf", PageSize = 2, PageNumber = 3 });
        Check(first.TotalCount == 5 && second.TotalCount == 5 && third.Items.Count == 1 && first.Items.Count == 2 &&
            first.Items.Select(x => x.FileId).Intersect(second.Items.Select(x => x.FileId)).Count() == 0 &&
            first.Items.Last().CreatedUtc < second.Items.First().CreatedUtc, "Stable bounded pagination and filtered count");
        Check((await repository.ListAsync(new FileQuery { OriginalName = "page.pdf", PageNumber = int.MaxValue })).Items.Count == 0, "Empty far page without integer overflow");
        Check((await repository.ListAsync(new FileQuery { OriginalName = "PAGE.pdf" })).TotalCount == 0 &&
              (await repository.ListAsync(new FileQuery { OriginalName = "page.pdf " })).TotalCount == 0, "Ordinal exact name matching including trailing spaces");
        Check((await repository.ListAsync(new FileQuery { OriginalName = "page.pdf", CreatedFromUtc = Created.AddMinutes(1), CreatedBeforeUtc = Created.AddMinutes(3), Extension = ".pdf" })).TotalCount == 2,
            "AND date and extension filters with inclusive/exclusive bounds");
        Check((await repository.ListAsync(new FileQuery { Status = FileStatus.Deleted, FileId = file.FileId })).TotalCount == 1 &&
              (await repository.ListAsync(new FileQuery { Status = null, FileId = file.FileId })).TotalCount == 1 &&
              (await repository.ListAsync(new FileQuery { FileId = file.FileId })).TotalCount == 0, "FileId and state filters, all states and active default");
        var injection = Metadata("x'; DROP TABLE arkyva.Files; --", FileStatus.Active);
        await repository.AddAsync(injection);
        Check((await repository.ListAsync(new FileQuery { OriginalName = injection.OriginalName })).Items.Single().FileId == injection.FileId &&
              (await repository.ListAsync(new FileQuery { OriginalName = "' OR 1=1 --" })).TotalCount == 0, "SQL injection strings treated as literal data");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => repository.AddAsync(Metadata("concurrent.pdf", FileStatus.Active))));
        Check((await repository.ListAsync(new FileQuery { OriginalName = "concurrent.pdf" })).TotalCount == 8, "Concurrent independent inserts");
        var same = Metadata();
        int[] results = await Task.WhenAll(TryInsert(repository, same), TryInsert(repository, same));
        Check(results.Sum() == 1, "Concurrent duplicate insert has one winner");
        var competing = Metadata("competing", FileStatus.Active); await repository.AddAsync(competing);
        var delete = await repository.GetAsync(competing.FileId); delete.Status = FileStatus.Deleted; delete.DeletedUtc = Created.AddHours(3);
        var fail = await repository.GetAsync(competing.FileId); fail.Status = FileStatus.Failed;
        await Task.WhenAll(TryUpdate(repository, delete), TryUpdate(repository, fail));
        Check((await repository.GetAsync(competing.FileId)).Status == FileStatus.Deleted, "Concurrent stale state change cannot resurrect Deleted");
        await Throws<SqlException>(() => Execute(sql, "INSERT INTO arkyva.Files (FileId,OriginalName,SizeBytes,ProviderId,StorageKey,Status) VALUES (NEWID(), N'x', -1, N'p', N'k', 'Pending');"));
        await Throws<SqlException>(() => Execute(sql, "INSERT INTO arkyva.Files (FileId,OriginalName,SizeBytes,ProviderId,StorageKey,Status) VALUES (NEWID(), N'x', 1, N'p', N'k', 'Deleted');"));
        await Throws<SqlException>(() => Execute(sql, "INSERT INTO arkyva.Files (FileId,OriginalName,SizeBytes,ProviderId,StorageKey,Status) VALUES (NEWID(), N'x', 1, N'p', N'k', 'Active ');"));
    }

    private static async Task<int> TryInsert(SqlServerMetadataRepository repository, FileMetadata metadata)
    {
        try { await repository.AddAsync(metadata); return 1; }
        catch (SqlException error) when (error.Number == 2601 || error.Number == 2627) { return 0; }
    }
    private static async Task TryUpdate(SqlServerMetadataRepository repository, FileMetadata metadata)
    {
        try { await repository.UpdateAsync(metadata); }
        catch (InvalidOperationException) { }
    }
    private static async Task Duplicate(Func<Task> operation)
    {
        try { await operation(); }
        catch (SqlException error) when (error.Number == 2601 || error.Number == 2627) { checks++; Console.WriteLine("PASS: Duplicate rejected by SQL Server"); return; }
        throw new Exception("Expected duplicate-key error");
    }
    private static async Task Execute(SqlConnection connection, string sql)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF; " + sql;
            await command.ExecuteNonQueryAsync();
        }
    }
    private static async Task Throws<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); } catch (T) { checks++; return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++; Console.WriteLine("PASS: " + message);
    }
}
