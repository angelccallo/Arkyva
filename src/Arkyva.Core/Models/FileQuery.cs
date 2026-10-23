using System;
using System.Collections.Generic;

namespace Arkyva.Core.Models
{
    /// <summary>Filtros combinados mediante AND. El servicio y el repositorio deben rechazar paginas invalidas con ArgumentOutOfRangeException.</summary>
    public sealed class FileQuery
    {
        /// <summary>Pagina basada en uno; debe ser mayor que cero.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Cantidad de elementos por pagina; debe ser mayor que cero.</summary>
        public int PageSize { get; set; } = 50;

        /// <summary>Identificador exacto; null omite el filtro.</summary>
        public Guid? FileId { get; set; }

        /// <summary>Estado exacto. Active por defecto; null incluye todos los estados en consultas de metadatos.</summary>
        public FileStatus? Status { get; set; } = FileStatus.Active;

        /// <summary>Inicio inclusivo del intervalo de creación UTC; null no establece límite inferior.</summary>
        public DateTime? CreatedFromUtc { get; set; }

        /// <summary>Fin exclusivo del intervalo de creación UTC; null no establece límite superior.</summary>
        public DateTime? CreatedBeforeUtc { get; set; }

        /// <summary>Nombre original exacto; null omite el filtro. Comparacion ordinal sensible a mayusculas.</summary>
        public string OriginalName { get; set; }

        /// <summary>Extension exacta con punto; null omite el filtro. Comparacion ordinal sensible a mayusculas.</summary>
        public string Extension { get; set; }

    }
}
