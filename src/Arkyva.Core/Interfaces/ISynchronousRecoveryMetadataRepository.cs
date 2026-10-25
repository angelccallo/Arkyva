using Arkyva.Core.Models;

namespace Arkyva.Core.Interfaces
{
    /// <summary>Native synchronous counterpart of the optional durable receipt/recovery capability.</summary>
    public interface ISynchronousRecoveryMetadataRepository : ISynchronousMetadataRepository, IRecoveryMetadataRepository
    {
        /// <summary>Records size/hash only after successful physical publication, using the observed Pending revision.</summary>
        bool TryRecordUpload(FileMetadata expected, long sizeBytes, string sha256);
        /// <summary>Activates only independently verified content with the observed revision and durable receipt.</summary>
        bool TryActivateRecovered(FileMetadata expected);
        /// <summary>Finds an exact technical provider/key record in any state or null; absence does not prove orphanhood.</summary>
        FileMetadata GetByStorageKey(string providerId, string storageKey);
    }
}
