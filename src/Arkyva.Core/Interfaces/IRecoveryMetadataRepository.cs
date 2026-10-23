using System.Threading.Tasks;
using Arkyva.Core.Models;

namespace Arkyva.Core.Interfaces
{
    /// <summary>Optional metadata capability for durable publication receipts and revision-checked recovery.</summary>
    public interface IRecoveryMetadataRepository : IMetadataRepository
    {
        /// <summary>After successful Save only: atomically records size/hash for Pending if revision still matches and no receipt exists. False means conflict. On success updates the supplied snapshot.</summary>
        Task<bool> TryRecordUploadAsync(FileMetadata expected, long sizeBytes, string sha256);

        /// <summary>After streaming verification only: activates Pending/Failed with a receipt and matching revision. False means conflict. On success updates the supplied snapshot.</summary>
        Task<bool> TryActivateRecoveredAsync(FileMetadata expected);

        /// <summary>Finds a technical record in any state by exact provider/key, or null. Absence alone does not prove orphanhood.</summary>
        Task<FileMetadata> GetByStorageKeyAsync(string providerId, string storageKey);
    }
}
