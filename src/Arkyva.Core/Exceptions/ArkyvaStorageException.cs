using System;

namespace Arkyva.Core.Exceptions
{
    /// <summary>A physical storage operation failed. InnerException is diagnostic and may contain sensitive provider details.</summary>
    public sealed class ArkyvaStorageException : Exception
    {
        /// <summary>Public identifier associated with the failure, or null for a list operation.</summary>
        public Guid? FileId { get; }

        /// <summary>Creates an error with a safe message, public identifier and original cause.</summary>
        public ArkyvaStorageException(string message, Guid? fileId, Exception innerException) : base(message, innerException)
        {
            FileId = fileId;
        }
    }
}
