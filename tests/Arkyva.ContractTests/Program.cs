using System;
using System.Linq;
using System.Threading.Tasks;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;

internal static class Program
{
    private static int Main()
    {
        try
        {
#if ARKYVA_NETSTANDARD
            const string expectedTarget = ".NETStandard,Version=v2.0";
#else
            const string expectedTarget = ".NETFramework,Version=v4.7.2";
#endif
            foreach (var assembly in new[] { typeof(IArkyvaService).Assembly,
                typeof(Arkyva.Storage.FileSystem.FileSystemStorageProvider).Assembly,
                typeof(Arkyva.Metadata.SqlServer.Repositories.SqlServerMetadataRepository).Assembly })
                Check(assembly.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
                    .Cast<System.Runtime.Versioning.TargetFrameworkAttribute>().Single().FrameworkName == expectedTarget,
                    "Selected target: " + assembly.GetName().Name + " " + expectedTarget);
            foreach (var contract in new[] { typeof(IArkyvaService), typeof(IStorageProvider), typeof(IMetadataRepository) })
                Check(contract.IsInterface && contract.GetMethods().All(m => typeof(Task).IsAssignableFrom(m.ReturnType)), contract.Name + " asincrono");
            Check(!typeof(Arkyva.Core.Models.FileInfo).GetProperties().Any(p => p.Name == "StorageKey" || p.Name == "ProviderId"), "API sin detalles fisicos");
            Check(typeof(IArkyvaService).GetMethod("GetInfoAsync").ReturnType == typeof(Task<Arkyva.Core.Models.FileInfo>), "API devuelve metadatos publicos");
            Check(typeof(IArkyvaService).GetMethod("DownloadAsync").ReturnType == typeof(Task<FileContent>) && typeof(IDisposable).IsAssignableFrom(typeof(FileContent)), "Descarga con contenido y metadatos desechables");
            Check(typeof(IStorageProvider).GetMethod("ReadAsync").ReturnType == typeof(Task<System.IO.Stream>), "Lectura mediante stream");
            Check(typeof(IRecoveryMetadataRepository).GetMethods().All(m => typeof(Task).IsAssignableFrom(m.ReturnType)) &&
                typeof(IStorageDiagnostics).GetMethod("InspectAsync").ReturnType == typeof(Task<StorageInventory>), "Capacidades de recuperación asíncronas");
            Check(!typeof(Arkyva.Core.Models.FileInfo).GetProperties().Any(p => p.Name == "CompletionSha256" || p.Name == "Revision"), "Recibo y revisión encapsulados");
            Check(new FileMetadata().Status == FileStatus.Pending, "Estado inicial no publicado");
            Check(typeof(ISynchronousArkyvaService).GetMethod("Upload").ReturnType == typeof(Guid) &&
                typeof(ISynchronousArkyvaService).GetMethod("Download").ReturnType == typeof(FileContent), "Optional synchronous service uses the existing models");
            Check(typeof(ISynchronousStorageProvider).GetMethod("Save").ReturnType == typeof(void) &&
                typeof(ISynchronousRecoveryMetadataRepository).GetMethod("TryRecordUpload").ReturnType == typeof(bool), "Optional synchronous storage and durable metadata contracts");
            Check(typeof(Arkyva.ArkyvaSynchronousExtensions).GetMethod("Upload").IsStatic, "Current exposes native sync dispatch without changing IArkyvaService");
            Check(new FileQuery().PageNumber == 1 && new FileQuery().PageSize > 0, "Paginacion inicial valida");
            Check(new PagedResult<FileMetadata>().Items.Count == 0, "Pagina inicial vacia");
            Console.WriteLine("Todas las comprobaciones de contratos pasaron en .NET Framework 4.7.2.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS: " + name);
    }
}
