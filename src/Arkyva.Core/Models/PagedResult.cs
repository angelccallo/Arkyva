using System;
using System.Collections.Generic;

namespace Arkyva.Core.Models
{
    /// <summary>Resultado de una pagina. Items nunca debe ser null.</summary>
    public sealed class PagedResult<T>
    {
        /// <summary>Elementos de la pagina en orden estable.</summary>
        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

        /// <summary>Total filtrado antes de aplicar paginacion.</summary>
        public long TotalCount { get; set; }

    }
}
