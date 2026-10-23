using System;

namespace Arkyva.Core.Models
{
    /// <summary>Explicit bounded maintenance settings; no work is scheduled automatically.</summary>
    public sealed class RecoveryOptions
    {
        /// <summary>Minimum creation age for metadata examination; age alone does not prove abandonment.</summary>
        public TimeSpan MinimumAge { get; set; } = TimeSpan.FromHours(2);
        /// <summary>True by default: diagnose proposed actions without modifying metadata or physical files.</summary>
        public bool DryRun { get; set; } = true;
        /// <summary>Records per query, 1 to 1000.</summary>
        public int PageSize { get; set; } = 100;
        /// <summary>Maximum metadata records examined in one invocation, 1 to 100000.</summary>
        public int MaximumRecords { get; set; } = 1000;
        /// <summary>Maximum physical entries visited, 1 to 100000.</summary>
        public int MaximumStorageEntries { get; set; } = 1000;
        /// <summary>Requests read-only inventory when the provider supports IStorageDiagnostics.</summary>
        public bool InspectStorage { get; set; } = true;
        /// <summary>Requests temporary cleanup. Without reliable cross-process ownership evidence, this version reports and preserves them.</summary>
        public bool CleanTemporaries { get; set; }
    }
}
