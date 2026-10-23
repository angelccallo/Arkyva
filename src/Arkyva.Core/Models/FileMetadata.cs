using System;
using System.Collections.Generic;

namespace Arkyva.Core.Models
{
    /// <summary>Registro tecnico para proveedores de metadatos; no se devuelve al consumidor.</summary>
    public sealed class FileMetadata : FileInfo
    {
        /// <summary>Optimistic concurrency revision; updated by successful conditional writes.</summary>
        public long Revision { get; set; }

        /// <summary>Lowercase SHA-256 receipt recorded only after successful physical publication; null for legacy or uncertain uploads.</summary>
        public string CompletionSha256 { get; set; }

        /// <summary>Identificador estable del proveedor configurado.</summary>
        public string ProviderId { get; set; }

        /// <summary>Clave interna generada por Arkyva, independiente del nombre original.</summary>
        public string StorageKey { get; set; }

    }
}
