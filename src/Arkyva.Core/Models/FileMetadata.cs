using System;
using System.Collections.Generic;

namespace Arkyva.Core.Models
{
    /// <summary>Registro tecnico para proveedores de metadatos; no se devuelve al consumidor.</summary>
    public sealed class FileMetadata : FileInfo
    {
        /// <summary>Identificador estable del proveedor configurado.</summary>
        public string ProviderId { get; set; }

        /// <summary>Clave interna generada por Arkyva, independiente del nombre original.</summary>
        public string StorageKey { get; set; }

    }
}
