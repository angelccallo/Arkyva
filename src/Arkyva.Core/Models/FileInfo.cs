using System;
using System.Collections.Generic;

namespace Arkyva.Core.Models
{
    /// <summary>Metadatos publicos de un archivo; no contiene ubicaciones ni detalles del proveedor.</summary>
    public class FileInfo
    {
        /// <summary>Identificador publico unico.</summary>
        public Guid FileId { get; set; }

        /// <summary>Nombre original descriptivo.</summary>
        public string OriginalName { get; set; }

        /// <summary>Extension con punto; vacia si no existe.</summary>
        public string Extension { get; set; }

        /// <summary>Tipo MIME; null cuando sea desconocido.</summary>
        public string MimeType { get; set; }

        /// <summary>Tamano no negativo en bytes.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Fecha de creacion en UTC.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>Fecha UTC de eliminacion logica o null.</summary>
        public DateTime? DeletedUtc { get; set; }

        /// <summary>Estado de la operacion y del archivo.</summary>
        public FileStatus Status { get; set; }

    }
}
