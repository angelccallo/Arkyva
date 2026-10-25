using System;
using System.IO;
using Arkyva.Core.Models;
using FileInfo = Arkyva.Core.Models.FileInfo;

namespace Arkyva.Core.Interfaces
{
    /// <summary>Optional native synchronous API. Existing asynchronous-only implementations remain valid.</summary>
    public interface ISynchronousArkyvaService : IArkyvaService
    {
        /// <summary>Stores content with the same receipts/state guarantees as UploadAsync. Leaves the input open.</summary>
        Guid Upload(Stream content, string fileName);
        /// <summary>Opens Active content. The consumer must dispose the returned FileContent.</summary>
        FileContent Download(Guid fileId);
        /// <summary>Returns public Active metadata or null, without opening content.</summary>
        FileInfo GetInfo(Guid fileId);
        /// <summary>Returns a bounded page using the same filters and ordering as ListAsync.</summary>
        PagedResult<FileInfo> List(FileQuery query);
        /// <summary>Checks Active metadata and physical availability; availability may change afterward.</summary>
        bool Exists(Guid fileId);
        /// <summary>Logically deletes without removing content; missing/already Deleted records are accepted.</summary>
        void Delete(Guid fileId);
    }
}
