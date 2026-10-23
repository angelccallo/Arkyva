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

        /// <summary>Nombre original exacto; null omite el filtro. Comparacion ordinal sensible a mayusculas.</summary>
        public string OriginalName { get; set; }

        /// <summary>Extension exacta con punto; null omite el filtro. Comparacion ordinal sensible a mayusculas.</summary>
        public string Extension { get; set; }

    }
}
