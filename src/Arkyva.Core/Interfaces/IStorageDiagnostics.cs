using System.Threading.Tasks;
using Arkyva.Core.Models;

namespace Arkyva.Core.Interfaces
{
    /// <summary>Optional bounded, read-only inventory of physical entries. Never infers abandonment or deletes content.</summary>
    public interface IStorageDiagnostics
    {
        /// <summary>Inspects at most maxEntries filesystem entries, including directories. Technical keys must not be exposed to end users.</summary>
        Task<StorageInventory> InspectAsync(int maxEntries);
    }
}
