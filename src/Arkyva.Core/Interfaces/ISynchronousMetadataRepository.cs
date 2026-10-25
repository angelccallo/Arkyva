using System;
using Arkyva.Core.Models;

namespace Arkyva.Core.Interfaces
{
    /// <summary>Optional synchronous metadata operations with the existing validation and revision semantics.</summary>
    public interface ISynchronousMetadataRepository : IMetadataRepository
    {
        /// <summary>Inserts a new record without replacing an existing identity.</summary>
        void Add(FileMetadata metadata);
        /// <summary>Returns a record in any state or null.</summary>
        FileMetadata Get(Guid fileId);
        /// <summary>Returns a stable bounded page and filtered total, Active by default.</summary>
        PagedResult<FileMetadata> List(FileQuery query);
        /// <summary>Checks whether an Active record exists.</summary>
        bool Exists(Guid fileId);
        /// <summary>Conditionally updates the observed revision; conflicts throw and successful writes update the snapshot revision.</summary>
        void Update(FileMetadata metadata);
    }
}
