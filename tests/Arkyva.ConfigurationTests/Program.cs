using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Arkyva;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Services;

internal static class Program
{
    private const string OfflineConnection = "Data Source=invalid.example;Initial Catalog=ArkyvaTests;Integrated Security=True;Connect Timeout=1";
    private static int passed;
    private static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine(passed + " configuration tests passed on net472 (no SQL connection)."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "Arkyva-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Test("Valid fluent configuration builds existing providers without opening SQL", () =>
            {
                string directory = Path.Combine(root, "valid");
                var configured = ArkyvaBuilder.Create().UseSqlServer(OfflineConnection).UseFileSystem(directory);
                Check(!Directory.Exists(directory));
                IArkyvaService service = configured.Build();
                Check(service is ArkyvaService && Directory.Exists(directory) && !(service is IDisposable));
                // Structural wiring check: production API does not expose provider instances.
                object storage = Field(service, "storage"); object metadata = Field(service, "metadata");
                Check(storage.GetType().FullName == "Arkyva.Storage.FileSystem.FileSystemStorageProvider");
                Check(metadata.GetType().FullName == "Arkyva.Metadata.SqlServer.Repositories.SqlServerMetadataRepository" && metadata is IRecoveryMetadataRepository);
                return Task.CompletedTask;
            });
            await Test("Missing connection fails before filesystem side effects", async () =>
            {
                string path = Path.Combine(root, "should-not-exist");
                var error = await Throws<InvalidOperationException>(() => Task.FromResult(ArkyvaBuilder.Create().UseFileSystem(path).Build()));
                Check(error.Message.Contains("UseSqlServer") && !Directory.Exists(path));
            });
            await Test("Missing storage and empty configuration are rejected", async () =>
            {
                await Throws<InvalidOperationException>(() => Task.FromResult(ArkyvaBuilder.Create().Build()));
                var error = await Throws<InvalidOperationException>(() => Task.FromResult(ArkyvaBuilder.Create().UseSqlServer(OfflineConnection).Build()));
                Check(error.Message.Contains("UseFileSystem"));
            });
            await Test("SQL values are validated syntactically without leaking the connection", async () =>
            {
                foreach (string value in new[] { null, "", " ", "invalid", "Data Source=invalid", "Initial Catalog=test", "Data Source=x;Initial Catalog=y;Password=private;InvalidKey=true", "Data Source=x;Initial Catalog=y;Connect Timeout=invalid", "Data Source=x;Initial Catalog=y;Connect Timeout=-1", "Data Source=x;Initial Catalog=y;Encrypt=invalid" })
                {
                    var error = await Throws<ArgumentException>(() => Task.FromResult(ArkyvaBuilder.Create().UseSqlServer(value)));
                    Check(!error.Message.Contains("private"));
                }
            });
            await Test("Invalid paths and file-as-directory rejected", async () =>
            {
                foreach (string path in new[] { null, "", " " })
                    await Throws<ArgumentException>(() => Task.FromResult(ArkyvaBuilder.Create().UseFileSystem(path)));
                foreach (string path in new[] { "relative", @"C:relative", @"\rooted", "bad\0path", @"\\?\C:\device" })
                    await Throws<ArgumentException>(() => Task.FromResult(ArkyvaBuilder.Create().UseSqlServer(OfflineConnection).UseFileSystem(path).Build()));
                string occupied = Path.Combine(root, "occupied"); File.WriteAllText(occupied, "file");
                await Throws<IOException>(() => Task.FromResult(ArkyvaBuilder.Create().UseSqlServer(OfflineConnection).UseFileSystem(occupied).Build()));
            });
            await Test("Calls may be made in either order", () =>
            {
                Check(ArkyvaBuilder.Create().UseFileSystem(Path.Combine(root, "order")).UseSqlServer(OfflineConnection).Build() != null);
                return Task.CompletedTask;
            });
            await Test("Immutable branches retain independent paths and source builder", async () =>
            {
                var original = ArkyvaBuilder.Create(); var sql = original.UseSqlServer(OfflineConnection);
                var left = sql.UseFileSystem(Path.Combine(root, "left")); var right = sql.UseFileSystem(Path.Combine(root, "right"));
                Check(!ReferenceEquals(left, right));
                var first = left.Build(); var second = right.Build();
                Check(!ReferenceEquals(first, second) && !ReferenceEquals(Field(first, "storage"), Field(second, "storage")));
                Check(Directory.Exists(Path.Combine(root, "left")) && Directory.Exists(Path.Combine(root, "right")));
                await Throws<InvalidOperationException>(() => Task.FromResult(original.Build()));
                await Throws<InvalidOperationException>(() => Task.FromResult(sql.Build()));
            });
            await Test("Repeated Build creates fresh providers without invalidating prior service", () =>
            {
                var builder = ArkyvaBuilder.Create().UseSqlServer(OfflineConnection).UseFileSystem(Path.Combine(root, "repeat"));
                var a = builder.Build(); var b = builder.Build();
                Check(!ReferenceEquals(a, b) && !ReferenceEquals(Field(a, "metadata"), Field(b, "metadata")));
                return Task.CompletedTask;
            });
            await Test("A reusable configuration can build concurrently", async () =>
            {
                var builder = ArkyvaBuilder.Create().UseSqlServer(OfflineConnection).UseFileSystem(Path.Combine(root, "parallel"));
                var services = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => builder.Build())));
                Check(services.Distinct().Count() == 8);
            });
            await Test("Built service is reusable and can be manually injected", async () =>
            {
                IArkyvaService service = ArkyvaBuilder.Create().UseSqlServer(OfflineConnection).UseFileSystem(Path.Combine(root, "reuse")).Build();
                var consumer = new Consumer(service); Check(ReferenceEquals(service, consumer.Service));
                // Invalid IDs exercise async API reuse without a database or fake SQL success.
                await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Throws<ArgumentException>(() => consumer.Service.GetInfoAsync(Guid.Empty))));
                await Throws<ArgumentException>(() => service.ExistsAsync(Guid.Empty));
            });
            await Test("No mutable static state or ASP.NET dependency", () =>
            {
                Check(typeof(ArkyvaBuilder).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Length == 0);
                Check(typeof(ArkyvaBuilder).Assembly.GetReferencedAssemblies().All(a => a.Name != "System.Web" && !a.Name.StartsWith("Microsoft.AspNet")));
                return Task.CompletedTask;
            });
        }
        finally
        {
            string full = Path.GetFullPath(root);
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Arkyva-config-", StringComparison.Ordinal)) throw new Exception("Unsafe cleanup target");
            Directory.Delete(full, true);
        }
    }
    private sealed class Consumer
    {
        internal IArkyvaService Service { get; }
        internal Consumer(IArkyvaService service) { Service = service; }
    }
    private static object Field(object service, string field) { return typeof(ArkyvaService).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service); }
    private static void Check(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Test(string name, Func<Task> action)
    { try { await action(); passed++; Console.WriteLine("PASS: " + name); } catch { Console.Error.WriteLine("FAIL: " + name); throw; } }
}
