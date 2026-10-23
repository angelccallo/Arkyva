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
            foreach (var contract in new[] { typeof(IArkyvaService), typeof(IStorageProvider), typeof(IMetadataRepository) })
                Check(contract.IsInterface && contract.GetMethods().All(m => typeof(Task).IsAssignableFrom(m.ReturnType)), contract.Name + " asincrono");
            Check(!typeof(Arkyva.Core.Models.FileInfo).GetProperties().Any(p => p.Name == "StorageKey" || p.Name == "ProviderId"), "API sin detalles fisicos");
            Check(typeof(IArkyvaService).GetMethod("GetInfoAsync").ReturnType == typeof(Task<Arkyva.Core.Models.FileInfo>), "API devuelve metadatos publicos");
            Check(typeof(IArkyvaService).GetMethod("DownloadAsync").ReturnType == typeof(Task<FileContent>) && typeof(IDisposable).IsAssignableFrom(typeof(FileContent)), "Descarga con contenido y metadatos desechables");
            Check(typeof(IStorageProvider).GetMethod("ReadAsync").ReturnType == typeof(Task<System.IO.Stream>), "Lectura mediante stream");
            Check(new FileMetadata().Status == FileStatus.Pending, "Estado inicial no publicado");
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
