using System;
using Arkyva.Core.Models;

namespace Arkyva.Core.Exceptions
{
    /// <summary>The file is not Active or cannot be served by the configured provider.</summary>
    public sealed class ArkyvaFileUnavailableException : InvalidOperationException
    {
        /// <summary>Public file identifier.</summary>
        public Guid FileId { get; }

        /// <summary>State observed in metadata.</summary>
        public FileStatus Status { get; }

        /// <summary>Creates a safe unavailable-file error.</summary>
        public ArkyvaFileUnavailableException(Guid fileId, FileStatus status)
            : base("The requested file is not available.") { FileId = fileId; Status = status; }
    }
}
