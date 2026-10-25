using System;
using System.IO;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using FileInfo = Arkyva.Core.Models.FileInfo;

namespace Arkyva
{
    /// <summary>Exposes optional native synchronous capabilities without changing the existing IArkyvaService contract.</summary>
    /// <remarks>These methods only dispatch to ISynchronousArkyvaService; they never block tasks or emulate synchronous I/O.</remarks>
    public static class ArkyvaSynchronousExtensions
    {
        /// <summary>Stores synchronously; requires a service with native synchronous capability.</summary>
        public static Guid Upload(this IArkyvaService service, Stream content, string fileName) { return Synchronous(service).Upload(content, fileName); }
        /// <summary>Downloads synchronously. Caller must dispose the result.</summary>
        public static FileContent Download(this IArkyvaService service, Guid fileId) { return Synchronous(service).Download(fileId); }
        /// <summary>Reads Active public metadata synchronously or returns null.</summary>
        public static FileInfo GetInfo(this IArkyvaService service, Guid fileId) { return Synchronous(service).GetInfo(fileId); }
        /// <summary>Lists public metadata synchronously with bounded pagination.</summary>
        public static PagedResult<FileInfo> List(this IArkyvaService service, FileQuery query) { return Synchronous(service).List(query); }
        /// <summary>Checks Active metadata and physical availability synchronously.</summary>
        public static bool Exists(this IArkyvaService service, Guid fileId) { return Synchronous(service).Exists(fileId); }
        /// <summary>Deletes logically and synchronously without physical removal.</summary>
        public static void Delete(this IArkyvaService service, Guid fileId) { Synchronous(service).Delete(fileId); }

        private static ISynchronousArkyvaService Synchronous(IArkyvaService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            var capability = service as ISynchronousArkyvaService;
            if (capability == null) throw new NotSupportedException("This service does not implement native synchronous operations. Use its Async API or implement ISynchronousArkyvaService.");
            return capability;
        }
    }
}
