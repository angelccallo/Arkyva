using System;
using System.IO;
using System.Threading.Tasks;
using Arkyva.Core.Models;
using FileInfo = Arkyva.Core.Models.FileInfo;

namespace Arkyva.Core.Interfaces
{
    /// <summary>API del consumidor basada en identificadores. Oculta claves y proveedores; el acceso al contenido requiere estado Active.</summary>
    public interface IArkyvaService
    {
        /// <summary>Guarda contenido y devuelve su identificador. No cierra el stream recibido; el nombre es solo descriptivo.</summary>
        Task<Guid> UploadAsync(Stream content, string fileName);

        /// <summary>Devuelve contenido y metadatos públicos. El consumidor debe liberar el resultado; ausentes y estados no disponibles producen excepciones específicas.</summary>
        Task<FileContent> DownloadAsync(Guid fileId);

        /// <summary>Devuelve metadatos publicos o null si no existe un archivo activo.</summary>
        Task<FileInfo> GetInfoAsync(Guid fileId);

        /// <summary>Lista metadatos públicos, activos por defecto, con filtros explícitos de estado y paginación; orden estable por creacion UTC y FileId.</summary>
        Task<PagedResult<FileInfo>> ListAsync(FileQuery query);

        /// <summary>Indica si el archivo está Active y su contenido físico está disponible en el proveedor configurado. No garantiza disponibilidad posterior.</summary>
        Task<bool> ExistsAsync(Guid fileId);

        /// <summary>Marca el archivo como eliminado sin borrar contenido fisico. Es idempotente incluso si no existe.</summary>
        Task DeleteAsync(Guid fileId);

    }
}
