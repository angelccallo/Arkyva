using System;
using System.IO;
using System.Threading.Tasks;
using Arkyva.Core.Models;

namespace Arkyva.Core.Interfaces
{
    /// <summary>Contrato de contenido fisico. Las claves son internas y relativas al proveedor; nunca nombres originales ni rutas publicas.</summary>
    public interface IStorageProvider
    {
        /// <summary>Guarda una clave nueva sin sobrescribir. No cierra content. Publica solo contenido completo; en conflicto lanza IOException.</summary>
        Task SaveAsync(string key, Stream content);

        /// <summary>Abre contenido existente desde el inicio. El llamador debe liberar el stream. Si no existe lanza FileNotFoundException.</summary>
        Task<Stream> ReadAsync(string key);

        /// <summary>Indica si existe contenido completo para la clave; los fallos del proveedor se propagan.</summary>
        Task<bool> ExistsAsync(string key);

        /// <summary>Elimina fisicamente la clave. Si ya no existe, termina correctamente. No representa la eliminacion logica del servicio.</summary>
        Task DeleteAsync(string key);

    }
}
