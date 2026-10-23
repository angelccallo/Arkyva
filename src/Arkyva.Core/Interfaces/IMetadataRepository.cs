using System;
using System.IO;
using System.Threading.Tasks;
using Arkyva.Core.Models;

namespace Arkyva.Core.Interfaces
{
    /// <summary>Persistencia de metadatos tecnicos. No accede al contenido ni ejecuta reglas de negocio.</summary>
    public interface IMetadataRepository
    {
        /// <summary>Inserta un registro nuevo. Un FileId duplicado es un error; no reemplaza registros.</summary>
        Task AddAsync(FileMetadata metadata);

        /// <summary>Obtiene el registro en cualquier estado o null si no existe.</summary>
        Task<FileMetadata> GetAsync(Guid fileId);

        /// <summary>Lista registros con filtros (activos por defecto; Status null incluye todos los estados) y orden por CreatedUtc y FileId ascendentes. TotalCount es el total filtrado antes de paginar.</summary>
        Task<PagedResult<FileMetadata>> ListAsync(FileQuery query);

        /// <summary>Indica si existe un registro activo para el identificador.</summary>
        Task<bool> ExistsAsync(Guid fileId);

        /// <summary>Actualiza un registro existente, incluidos estado y fecha de eliminacion. Requiere la Revision leída; una actualización correcta incrementa Revision en el snapshot. Ausencia o conflicto lanza InvalidOperationException; no realiza upsert.</summary>
        Task UpdateAsync(FileMetadata metadata);

    }
}
