using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arkyva.Core.Exceptions;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using FileInfo = Arkyva.Core.Models.FileInfo;

namespace Arkyva.Core.Services
{
    /// <summary>Coordinates metadata and physical storage without distributed transactions or physical cleanup.</summary>
    public sealed class ArkyvaService : IArkyvaService
    {
        private readonly IStorageProvider storage;
        private readonly IMetadataRepository metadata;
        private readonly string providerId;

        /// <summary>Creates a service for one provider identity. Use the same identity for the same repository across restarts.</summary>
        public ArkyvaService(IStorageProvider storage, IMetadataRepository metadata, string providerId)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 100)
                throw new ArgumentException("A provider identity of at most 100 characters is required.", nameof(providerId));
            this.storage = storage; this.metadata = metadata; this.providerId = providerId;
        }

        /// <summary>Registers Pending, streams content once, then activates with the actual transferred size. Leaves input open.</summary>
        public async Task<Guid> UploadAsync(Stream content, string fileName)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (!content.CanRead) throw new ArgumentException("A readable stream is required.", nameof(content));
            string extension = ValidateName(fileName);
            Guid id = Guid.NewGuid();
            DateTime created = DateTime.UtcNow;
            var pending = new FileMetadata
            {
                FileId = id, OriginalName = fileName, Extension = extension,
                MimeType = "application/octet-stream", SizeBytes = 0,
                ProviderId = providerId, StorageKey = created.ToString("yyyy/MM/", CultureInfo.InvariantCulture) + id.ToString("N") + ".bin",
                Status = FileStatus.Pending, CreatedUtc = created
            };
            try { await metadata.AddAsync(pending).ConfigureAwait(false); }
            catch (Exception error) { throw new ArkyvaMetadataException("Could not register the upload; its result may be uncertain.", id, error); }

            long size;
            using (var counted = new CountingReadStream(content))
            {
                try { await storage.SaveAsync(pending.StorageKey, counted).ConfigureAwait(false); }
                catch (Exception error)
                {
                    var failed = Copy(pending); failed.Status = FileStatus.Failed;
                    Exception cause = error;
                    try { await metadata.UpdateAsync(failed).ConfigureAwait(false); }
                    catch (Exception stateError) { cause = new AggregateException(error, stateError); }
                    // Never delete a key whose publication result could be uncertain.
                    throw new ArkyvaStorageException("Could not store the upload. Its content has not been removed automatically.", id, cause);
                }
                size = counted.BytesRead;
            }
            var active = Copy(pending); active.Status = FileStatus.Active; active.SizeBytes = size;
            try { await metadata.UpdateAsync(active).ConfigureAwait(false); }
            catch (Exception error)
            {
                // A server may have committed before the client observed the failure.
                // Do not demote, retry or delete anything after uncertain activation.
                throw new ArkyvaMetadataException("Could not confirm upload activation; its result may be uncertain.", id, error);
            }
            return id;
        }

        /// <summary>Opens Active content and returns an owning result containing only public metadata.</summary>
        public async Task<FileContent> DownloadAsync(Guid fileId)
        {
            ValidateId(fileId);
            FileMetadata record = await GetRecord(fileId).ConfigureAwait(false);
            if (record == null) throw new ArkyvaFileNotFoundException(fileId);
            if (record.Status != FileStatus.Active || record.ProviderId != providerId)
                throw new ArkyvaFileUnavailableException(fileId, record.Status);
            Stream stream;
            try { stream = await storage.ReadAsync(record.StorageKey).ConfigureAwait(false); }
            catch (FileNotFoundException error) { throw new ArkyvaFileNotFoundException(fileId, error); }
            catch (DirectoryNotFoundException error) { throw new ArkyvaFileNotFoundException(fileId, error); }
            catch (Exception error) { throw new ArkyvaStorageException("Could not open the requested content.", fileId, error); }
            if (stream == null) throw new ArkyvaStorageException("The provider did not return content.", fileId, new InvalidOperationException("Invalid provider result."));
            return new FileContent(PublicInfo(record), new ContentReadStream(stream, fileId));
        }

        /// <summary>Returns an Active public metadata snapshot or null. Does not access physical content.</summary>
        public async Task<FileInfo> GetInfoAsync(Guid fileId)
        {
            ValidateId(fileId);
            FileMetadata record = await GetRecord(fileId).ConfigureAwait(false);
            return record == null || record.Status != FileStatus.Active ? null : PublicInfo(record);
        }

        /// <summary>Returns a bounded page of public metadata. Active by default; explicit states, including null for all, are supported.</summary>
        public async Task<PagedResult<FileInfo>> ListAsync(FileQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            // Validate the snapshot actually passed to deferred provider work.
            var snapshot = new FileQuery { PageNumber = query.PageNumber, PageSize = query.PageSize,
                FileId = query.FileId, Status = query.Status, OriginalName = query.OriginalName,
                Extension = query.Extension, CreatedFromUtc = query.CreatedFromUtc, CreatedBeforeUtc = query.CreatedBeforeUtc };
            ValidateQuery(snapshot);
            PagedResult<FileMetadata> page;
            try { page = await metadata.ListAsync(snapshot).ConfigureAwait(false); }
            catch (Exception error) { throw new ArkyvaMetadataException("Could not list file metadata.", null, error); }
            return new PagedResult<FileInfo> { TotalCount = page.TotalCount, Items = page.Items.Select(PublicInfo).ToArray() };
        }

        /// <summary>Checks Active metadata, provider identity and physical existence. Availability may change immediately afterward.</summary>
        public async Task<bool> ExistsAsync(Guid fileId)
        {
            ValidateId(fileId);
            FileMetadata record = await GetRecord(fileId).ConfigureAwait(false);
            if (record == null || record.Status != FileStatus.Active || record.ProviderId != providerId) return false;
            try { return await storage.ExistsAsync(record.StorageKey).ConfigureAwait(false); }
            catch (Exception error) { throw new ArkyvaStorageException("Could not verify content availability.", fileId, error); }
        }

        /// <summary>Marks an existing record Deleted without deleting content; missing/already Deleted records are accepted.</summary>
        public async Task DeleteAsync(Guid fileId)
        {
            ValidateId(fileId);
            FileMetadata record = await GetRecord(fileId).ConfigureAwait(false);
            if (record == null || record.Status == FileStatus.Deleted) return;
            if (!Enum.IsDefined(typeof(FileStatus), record.Status)) throw new ArkyvaFileUnavailableException(fileId, record.Status);
            var deleted = Copy(record); deleted.Status = FileStatus.Deleted;
            DateTime now = DateTime.UtcNow;
            deleted.DeletedUtc = now < record.CreatedUtc ? record.CreatedUtc : now;
            try { await metadata.UpdateAsync(deleted).ConfigureAwait(false); }
            catch (Exception error)
            {
                // A concurrent delete or an acknowledged-late commit may already have succeeded.
                try
                {
                    FileMetadata latest = await metadata.GetAsync(fileId).ConfigureAwait(false);
                    if (latest != null && latest.Status == FileStatus.Deleted) return;
                }
                catch (Exception confirmationError)
                { throw new ArkyvaMetadataException("Could not confirm logical deletion.", fileId, new AggregateException(error, confirmationError)); }
                throw new ArkyvaMetadataException("Could not mark the file as deleted.", fileId, error);
            }
        }

        private async Task<FileMetadata> GetRecord(Guid id)
        {
            try { return await metadata.GetAsync(id).ConfigureAwait(false); }
            catch (Exception error) { throw new ArkyvaMetadataException("Could not read file metadata.", id, error); }
        }

        private static FileInfo PublicInfo(FileMetadata source)
        {
            return new FileInfo { FileId = source.FileId, OriginalName = source.OriginalName,
                Extension = source.Extension, MimeType = source.MimeType, SizeBytes = source.SizeBytes,
                CreatedUtc = source.CreatedUtc, DeletedUtc = source.DeletedUtc, Status = source.Status };
        }

        private static FileMetadata Copy(FileMetadata source)
        {
            return new FileMetadata { FileId = source.FileId, OriginalName = source.OriginalName,
                Extension = source.Extension, MimeType = source.MimeType, SizeBytes = source.SizeBytes,
                CreatedUtc = source.CreatedUtc, DeletedUtc = source.DeletedUtc, Status = source.Status,
                ProviderId = source.ProviderId, StorageKey = source.StorageKey };
        }

        private static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name != name.Trim() || name == "." || name == ".." || name.EndsWith(".", StringComparison.Ordinal) ||
                name.Any(c => char.IsControl(c) || "/\\:*?\"<>|".IndexOf(c) >= 0))
                throw new ArgumentException("A file name without path components is required (maximum 255 characters).", nameof(name));
            int dot = name.LastIndexOf('.');
            string extension = dot > 0 ? name.Substring(dot) : string.Empty;
            if (extension.Length > 30) throw new ArgumentException("File extension exceeds 30 characters.", nameof(name));
            return extension;
        }

        private static void ValidateId(Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("FileId must not be empty.", nameof(id));
        }

        private static void ValidateQuery(FileQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.PageNumber <= 0) throw new ArgumentOutOfRangeException(nameof(query.PageNumber));
            if (query.PageSize <= 0 || query.PageSize > 1000) throw new ArgumentOutOfRangeException(nameof(query.PageSize));
            if (query.FileId.HasValue) ValidateId(query.FileId.Value);
            if (query.Status.HasValue && !Enum.IsDefined(typeof(FileStatus), query.Status.Value)) throw new ArgumentOutOfRangeException(nameof(query.Status));
            if ((query.OriginalName != null && query.OriginalName.Length > 255) || (query.Extension != null && query.Extension.Length > 30))
                throw new ArgumentException("Filter exceeds supported length.", nameof(query));
            if ((query.CreatedFromUtc.HasValue && query.CreatedFromUtc.Value.Kind != DateTimeKind.Utc) ||
                (query.CreatedBeforeUtc.HasValue && query.CreatedBeforeUtc.Value.Kind != DateTimeKind.Utc) ||
                (query.CreatedFromUtc.HasValue && query.CreatedBeforeUtc.HasValue && query.CreatedFromUtc >= query.CreatedBeforeUtc))
                throw new ArgumentException("A non-empty UTC creation interval is required.", nameof(query));
        }
    }
}
