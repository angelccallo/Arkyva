using System;
using System.IO;

namespace Arkyva.Core.Models
{
    /// <summary>Owns a downloaded stream and its public metadata; dispose the result after use.</summary>
    public sealed class FileContent : IDisposable
    {
        /// <summary>Public metadata snapshot without storage keys or provider details.</summary>
        public FileInfo Info { get; }

        /// <summary>Readable content stream owned by this result.</summary>
        public Stream Content { get; }

        /// <summary>Creates a result that takes ownership of the stream.</summary>
        public FileContent(FileInfo info, Stream content)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            if (content == null) throw new ArgumentNullException(nameof(content));
            // Always construct a base DTO: never retain a FileMetadata reference.
            Info = new FileInfo { FileId = info.FileId, OriginalName = info.OriginalName,
                Extension = info.Extension, MimeType = info.MimeType, SizeBytes = info.SizeBytes,
                CreatedUtc = info.CreatedUtc, DeletedUtc = info.DeletedUtc, Status = info.Status };
            Content = content;
        }

        /// <summary>Releases the content stream.</summary>
        public void Dispose() { Content.Dispose(); }
    }
}
