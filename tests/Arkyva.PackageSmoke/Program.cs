using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arkyva;
using Arkyva.Core.Exceptions;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using System.ServiceModel;

// Framework reference only: no WCF dependency is added to any Arkyva library.
[ServiceContract]
public interface IPackageWcfSmoke
{
    [OperationContract]
    Guid Upload(string originalName, string base64);
    [OperationContract]
    Task<Guid> UploadAsync(string originalName, string base64);
}

internal static class Program
{
    private static int checks;
    private static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine("Package consumer passed checks: " + checks); return 0; }
        catch (Exception error) { Console.Error.WriteLine("Package consumer failed: " + error.GetType().Name); return 1; }
    }
    private static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "Arkyva-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string configured = Environment.GetEnvironmentVariable("ARKYVA_PACKAGE_TEST_CONNECTION");
            IArkyvaService service = ArkyvaBuilder.Create().UseSqlServer(configured ??
                "Data Source=invalid.example;Initial Catalog=package_validation;Integrated Security=True;Connect Timeout=1").UseFileSystem(root).Build();
            Check(service != null && !(service is IDisposable), "Build from NuGet on net472");
            Check(typeof(IPackageWcfSmoke).GetCustomAttributes(typeof(ServiceContractAttribute), false).Length == 1,
                "WCF contract and public Arkyva API compile together on net472");
            await Throws<InvalidOperationException>(() => Task.FromResult(ArkyvaHost.Current));
            ArkyvaHost.Configure(options =>
            {
                options.UseSqlServer(configured ?? "Data Source=invalid.example;Initial Catalog=package_validation;Integrated Security=True;Connect Timeout=1");
                options.UseFileSystem(root);
            });
            Check(!ReferenceEquals(service, ArkyvaHost.Current) && ReferenceEquals(ArkyvaHost.Current, ArkyvaHost.Current), "Host from NuGet is shared and independent of builder");
            service = ArkyvaHost.Current;
            await Throws<InvalidOperationException>(() => { ArkyvaHost.Configure(o => { }); return Task.CompletedTask; });
            await Throws<ArgumentException>(() => service.GetInfoAsync(Guid.Empty));
            await Throws<ArgumentException>(() => service.DownloadAsync(Guid.Empty));
            await Throws<ArgumentException>(() => service.ExistsAsync(Guid.Empty));
            await Throws<ArgumentException>(() => service.DeleteAsync(Guid.Empty));
            await Throws<ArgumentException>(() => service.UploadAsync(null, "a.bin"));
            await Throws<ArgumentException>(() => service.ListAsync(new FileQuery { PageSize = 0 }));
            ThrowsSync<ArgumentException>(() => ArkyvaHost.Current.Upload(null, "x.bin"));
            ThrowsSync<ArgumentException>(() => ArkyvaHost.Current.Download(Guid.Empty));
            ThrowsSync<ArgumentException>(() => ArkyvaHost.Current.GetInfo(Guid.Empty));
            ThrowsSync<ArgumentException>(() => ArkyvaHost.Current.Exists(Guid.Empty));
            ThrowsSync<ArgumentException>(() => ArkyvaHost.Current.Delete(Guid.Empty));
            ThrowsSync<ArgumentException>(() => ArkyvaHost.Current.List(new FileQuery { PageSize = 0 }));
            Check(service is ISynchronousArkyvaService, "Current exposes native sync capability without changing its existing interface");
            if (string.IsNullOrWhiteSpace(configured))
            {
                Console.WriteLine("SKIP packaged SQL E2E: no isolated test connection supplied.");
                return;
            }
            byte[] expected = new byte[] { 1, 2, 3, 4, 5 };
            Guid id;
            using (var input = new MemoryStream(expected)) id = await service.UploadAsync(input, "package.bin");
            using (FileContent content = await service.DownloadAsync(id))
            using (var copy = new MemoryStream())
            {
                await content.Content.CopyToAsync(copy);
                Check(copy.ToArray().SequenceEqual(expected) && content.Info.SizeBytes == expected.Length, "Packaged upload/download integrity");
            }
            Check((await service.GetInfoAsync(id)).OriginalName == "package.bin", "Packaged GetInfo");
            Check((await service.ListAsync(new FileQuery { FileId = id })).TotalCount == 1, "Packaged List");
            Check(await service.ExistsAsync(id), "Packaged Exists");
            await service.DeleteAsync(id);
            Check(!await service.ExistsAsync(id) && await service.GetInfoAsync(id) == null, "Packaged logical Delete");
            await Throws<ArkyvaFileUnavailableException>(() => service.DownloadAsync(id));
            Check(Directory.GetFiles(root, "*.bin", SearchOption.AllDirectories).Length == 1, "Packaged Delete retains physical content");
            Guid native;
            using (var input = new MemoryStream(expected)) native = ArkyvaHost.Current.Upload(input, "native-package.bin");
            using (var content = await ArkyvaHost.Current.DownloadAsync(native))
            using (var copy = new MemoryStream()) { await content.Content.CopyToAsync(copy); Check(copy.ToArray().SequenceEqual(expected), "Packaged Upload interoperates with DownloadAsync"); }
            Guid fromAsync;
            using (var input = new MemoryStream(expected)) fromAsync = await service.UploadAsync(input, "async-package.bin");
            using (var content = ArkyvaHost.Current.Download(fromAsync))
            using (var copy = new MemoryStream()) { content.Content.CopyTo(copy); Check(copy.ToArray().SequenceEqual(expected), "Packaged UploadAsync interoperates with native Download"); }
            Check(service.GetInfo(native).SizeBytes == expected.Length && service.Exists(native) && service.List(new FileQuery { FileId = native }).TotalCount == 1, "Packaged synchronous metadata, availability and pagination");
            service.Delete(native); service.Delete(native);
            Check(service.GetInfo(native) == null && !await service.ExistsAsync(native), "Packaged synchronous Delete is logical and idempotent");
            ThrowsSync<ArkyvaFileUnavailableException>(() => service.Download(native));
            await service.DeleteAsync(fromAsync);
            Check(!service.Exists(fromAsync) && Directory.GetFiles(root, "*.bin", SearchOption.AllDirectories).Length == 3, "Packaged mixed deletion retains definitive content");
        }
        finally
        {
            string full = Path.GetFullPath(root);
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Arkyva-package-", StringComparison.Ordinal)) throw new Exception("Unsafe cleanup target");
            Directory.Delete(full, true);
        }
    }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
    private static void ThrowsSync<T>(Action action) where T : Exception
    { try { action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
}
