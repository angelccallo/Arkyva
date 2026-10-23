using System;
using System.IO;

namespace Arkyva.Core.Exceptions
{
    /// <summary>The metadata record or the physical content was not found.</summary>
    public sealed class ArkyvaFileNotFoundException : FileNotFoundException
    {
        /// <summary>Public file identifier.</summary>
        public Guid FileId { get; }

        /// <summary>Creates a missing-file error without exposing a physical path.</summary>
        public ArkyvaFileNotFoundException(Guid fileId, Exception innerException = null)
            : base("The requested file was not found.", innerException) { FileId = fileId; }
    }
}
