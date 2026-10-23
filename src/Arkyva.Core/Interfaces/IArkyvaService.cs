using System;
using System.IO;
using System.Threading.Tasks;
using Arkyva.Core.Models;
using FileInfo = Arkyva.Core.Models.FileInfo;

namespace Arkyva.Core.Interfaces
{
    /// <summary>API del consumidor basada en identificadores. Oculta claves y proveedores; solo expone archivos activos.</summary>
    public interface IArkyvaService
    {
        /// <summary>Guarda contenido y devuelve su identificador. No cierra el stream recibido; el nombre es solo descriptivo.</summary>
        Task<Guid> UploadAsync(Stream content, string fileName);

        /// <summary>Devuelve un stream que el consumidor debe liberar. Si el archivo no esta activo, lanza FileNotFoundException.</summary>
        Task<Stream> DownloadAsync(Guid fileId);

        /// <summary>Devuelve metadatos publicos o null si no existe un archivo activo.</summary>
        Task<FileInfo> GetInfoAsync(Guid fileId);

        /// <summary>Lista archivos activos segun filtros y paginacion; orden estable por creacion UTC y FileId.</summary>
        Task<PagedResult<FileInfo>> ListAsync(FileQuery query);

        /// <summary>Indica si el archivo esta activo en el repositorio.</summary>
        Task<bool> ExistsAsync(Guid fileId);

        /// <summary>Marca el archivo como eliminado sin borrar contenido fisico. Es idempotente incluso si no existe.</summary>
        Task DeleteAsync(Guid fileId);

    }
}
