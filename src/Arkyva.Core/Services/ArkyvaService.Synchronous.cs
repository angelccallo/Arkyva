using System;
using System.IO;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using Arkyva.Core.Exceptions;
using FileInfo = Arkyva.Core.Models.FileInfo;

namespace Arkyva.Core.Services
{
    public sealed partial class ArkyvaService
    {
        /// <summary>Native synchronous counterpart of UploadAsync, with the same resource and consistency guarantees.</summary>
        public Guid Upload(Stream content, string fileName)
        {
            var pending = CreatePending(content, fileName);
            Guid id = pending.FileId;
            EnsureSynchronousUpload();
            try { SynchronousMetadata.Add(pending); }
            catch (Exception error) { throw new ArkyvaMetadataException("Could not register the upload; its result may be uncertain.", id, error); }

            long size;
            string digest;
            using (var counted = new CountingReadStream(content))
            {
                try
                {
                    SynchronousStorage.Save(pending.StorageKey, counted);
                    digest = counted.CompleteHash();
                }
                catch (Exception error)
                {
                    var failed = FailedSnapshot(pending);
                    Exception cause = error;
                    try { SynchronousMetadata.Update(failed); }
                    catch (Exception stateError) { cause = new AggregateException(error, stateError); }
                    // Never delete a key whose publication result could be uncertain.
                    throw new ArkyvaStorageException("Could not store the upload. Its content has not been removed automatically.", id, cause);
                }
                finally { counted.ReleaseHash(); }
                size = counted.BytesRead;
            }
            var recoverable = metadata as ISynchronousRecoveryMetadataRepository;
            if (recoverable != null)
            {
                try
                {
                    if (!recoverable.TryRecordUpload(pending, size, digest))
                        throw new InvalidOperationException("Upload receipt revision conflict.");
                }
                catch (Exception error)
                { throw new ArkyvaMetadataException("Could not confirm the upload receipt; its result may be uncertain.", id, error); }
            }
            var active = ActiveSnapshot(pending, size);
            try { SynchronousMetadata.Update(active); }
            catch (Exception error)
            {
                // A server may have committed before the client observed the failure.
                // Do not demote, retry or delete anything after uncertain activation.
                throw new ArkyvaMetadataException("Could not confirm upload activation; its result may be uncertain.", id, error);
            }
            return id;
        }

        /// <summary>Native synchronous counterpart of DownloadAsync, with the same resource and consistency guarantees.</summary>
        public FileContent Download(Guid fileId)
        {
            ValidateId(fileId);
            FileMetadata record = GetRecord(fileId);
            EnsureDownloadAvailable(record, fileId);
            var provider = SynchronousStorage;
            Stream stream;
            try { stream = provider.Read(record.StorageKey); }
            catch (FileNotFoundException error) { throw new ArkyvaFileNotFoundException(fileId, error); }
            catch (DirectoryNotFoundException error) { throw new ArkyvaFileNotFoundException(fileId, error); }
            catch (Exception error) { throw new ArkyvaStorageException("Could not open the requested content.", fileId, error); }
            if (stream == null) throw new ArkyvaStorageException("The provider did not return content.", fileId, new InvalidOperationException("Invalid provider result."));
            return new FileContent(PublicInfo(record), new ContentReadStream(stream, fileId));
        }

        /// <summary>Native synchronous counterpart of GetInfoAsync, with the same resource and consistency guarantees.</summary>
        public FileInfo GetInfo(Guid fileId)
        {
            ValidateId(fileId);
            FileMetadata record = GetRecord(fileId);
            return record == null || record.Status != FileStatus.Active ? null : PublicInfo(record);
        }

        /// <summary>Native synchronous counterpart of ListAsync, with the same resource and consistency guarantees.</summary>
        public PagedResult<FileInfo> List(FileQuery query)
        {
            var snapshot = QuerySnapshot(query);
            var repository = SynchronousMetadata;
            PagedResult<FileMetadata> page;
            try { page = repository.List(snapshot); }
            catch (Exception error) { throw new ArkyvaMetadataException("Could not list file SynchronousMetadata.", null, error); }
            return PublicPage(page);
        }

        /// <summary>Native synchronous counterpart of ExistsAsync, with the same resource and consistency guarantees.</summary>
        public bool Exists(Guid fileId)
        {
            ValidateId(fileId);
            FileMetadata record = GetRecord(fileId);
            if (record == null || record.Status != FileStatus.Active || record.ProviderId != providerId) return false;
            var provider = SynchronousStorage;
            try { return provider.Exists(record.StorageKey); }
            catch (Exception error) { throw new ArkyvaStorageException("Could not verify content availability.", fileId, error); }
        }

        /// <summary>Native synchronous counterpart of DeleteAsync, with the same resource and consistency guarantees.</summary>
        public void Delete(Guid fileId)
        {
            ValidateId(fileId);
            FileMetadata record = GetRecord(fileId);
            if (record == null || record.Status == FileStatus.Deleted) return;
            var deleted = DeletedSnapshot(record);
            try { SynchronousMetadata.Update(deleted); }
            catch (Exception error)
            {
                // A concurrent delete or an acknowledged-late commit may already have succeeded.
                try
                {
                    FileMetadata latest = SynchronousMetadata.Get(fileId);
                    if (latest != null && latest.Status == FileStatus.Deleted) return;
                }
                catch (Exception confirmationError)
                { throw new ArkyvaMetadataException("Could not confirm logical deletion.", fileId, new AggregateException(error, confirmationError)); }
                throw new ArkyvaMetadataException("Could not mark the file as deleted.", fileId, error);
            }
        }

        private ISynchronousStorageProvider SynchronousStorage
        {
            get
            {
                var capability = storage as ISynchronousStorageProvider;
                if (capability == null) throw new NotSupportedException("The storage provider does not support native synchronous I/O.");
                return capability;
            }
        }
        private ISynchronousMetadataRepository SynchronousMetadata
        {
            get
            {
                var capability = metadata as ISynchronousMetadataRepository;
                if (capability == null) throw new NotSupportedException("The metadata repository does not support native synchronous I/O.");
                return capability;
            }
        }
        private void EnsureSynchronousUpload()
        {
            if (!(storage is ISynchronousStorageProvider) || !(metadata is ISynchronousMetadataRepository) ||
                (metadata is IRecoveryMetadataRepository && !(metadata is ISynchronousRecoveryMetadataRepository)))
                throw new NotSupportedException("Synchronous upload requires native synchronous storage, metadata and any existing recovery capability. No record has been registered.");
        }
        private FileMetadata GetRecord(Guid id)
        {
            var repository = SynchronousMetadata;
            try { return repository.Get(id); }
            catch (Exception error) { throw new ArkyvaMetadataException("Could not read file metadata.", id, error); }
        }
    }
}
