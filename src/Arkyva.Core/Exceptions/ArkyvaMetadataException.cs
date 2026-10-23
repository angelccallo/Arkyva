using System;

namespace Arkyva.Core.Exceptions
{
    /// <summary>Metadata persistence failed. The result may be uncertain; inspect InnerException only in trusted diagnostics.</summary>
    public sealed class ArkyvaMetadataException : Exception
    {
        /// <summary>Public identifier associated with the failure, or null for a list operation.</summary>
        public Guid? FileId { get; }

        /// <summary>Creates an error with a safe message, public identifier and original cause.</summary>
        public ArkyvaMetadataException(string message, Guid? fileId, Exception innerException) : base(message, innerException)
        {
            FileId = fileId;
        }
    }
}
