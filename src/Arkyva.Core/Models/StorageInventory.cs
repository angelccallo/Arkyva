using System;
using System.Collections.Generic;

namespace Arkyva.Core.Models
{
    /// <summary>Technical, provider-side inventory; keys are only for internal maintenance.</summary>
    public sealed class StorageInventory
    {
        /// <summary>Bounded observed file entries.</summary>
        public IReadOnlyList<StorageInventoryEntry> Entries { get; set; } = Array.Empty<StorageInventoryEntry>();
        /// <summary>True if traversal stopped before inspecting all entries.</summary>
        public bool Truncated { get; set; }
        /// <summary>Number of access/I/O failures; no sensitive paths or exception messages are included.</summary>
        public int Errors { get; set; }
        /// <summary>Number of reparse points conservatively skipped.</summary>
        public int LinksSkipped { get; set; }
    }

    /// <summary>A technical observation, never proof that a file is orphaned or abandoned.</summary>
    public sealed class StorageInventoryEntry
    {
        /// <summary>Provider-relative internal key, not a public API identifier.</summary>
        public string Key { get; set; }
        /// <summary>True only for the exact reserved temporary name pattern.</summary>
        public bool IsTemporary { get; set; }
        /// <summary>Last write observation in UTC; not a lease or abandonment signal.</summary>
        public DateTime LastWriteUtc { get; set; }
    }
}
