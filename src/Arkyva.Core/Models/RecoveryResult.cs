using System;
using System.Collections.Generic;

namespace Arkyva.Core.Models
{
    /// <summary>Safe diagnostic item without physical paths, storage keys, credentials or raw exceptions.</summary>
    public sealed class RecoveryFinding
    {
        /// <summary>Stable diagnostic code, suitable for application logging.</summary>
        public string Code { get; internal set; }
        /// <summary>Public identifier, when known.</summary>
        public Guid? FileId { get; internal set; }
        /// <summary>Opaque SHA-256 reference to an inventory key; does not reveal its path.</summary>
        public string Reference { get; internal set; }
        /// <summary>Exception type name only, when applicable. No original message is emitted.</summary>
        public string ErrorType { get; internal set; }
    }

    /// <summary>Actual and proposed actions for one bounded manual maintenance invocation.</summary>
    public sealed class RecoveryResult
    {
        /// <summary>Metadata records examined.</summary>
        public int RecordsExamined { get; internal set; }
        /// <summary>Verified records actually activated.</summary>
        public int RecordsRecovered { get; internal set; }
        /// <summary>Records actually changed to Failed.</summary>
        public int RecordsMarkedFailed { get; internal set; }
        /// <summary>Verified activations proposed in dry run.</summary>
        public int ProposedRecoveries { get; internal set; }
        /// <summary>Failed transitions proposed in dry run.</summary>
        public int ProposedFailures { get; internal set; }
        /// <summary>Reserved temporary files observed.</summary>
        public int TemporariesIdentified { get; internal set; }
        /// <summary>Temporaries safely deleted; zero until ownership can be proven across processes.</summary>
        public int TemporariesDeleted { get; internal set; }
        /// <summary>Operations skipped due to uncertainty, unsupported capability or concurrency.</summary>
        public int OperationsSkipped { get; internal set; }
        /// <summary>Errors encountered without exposing original sensitive messages.</summary>
        public int Errors { get; internal set; }
        /// <summary>True when either configured scan bound was reached.</summary>
        public bool Truncated { get; internal set; }
        /// <summary>Safe observations and proposed/actual actions.</summary>
        public IReadOnlyList<RecoveryFinding> Findings { get; internal set; } = Array.Empty<RecoveryFinding>();
    }
}
