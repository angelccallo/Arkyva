using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;

namespace Arkyva.Core.Services
{
    /// <summary>Manual receipt-based recovery and conservative diagnostics; never deletes physical files.</summary>
    public sealed class StorageRecoveryService
    {
        private readonly IStorageProvider storage;
        private readonly IRecoveryMetadataRepository metadata;
        private readonly string providerId;

        /// <summary>Creates maintenance for exactly one provider identity and a revision-aware metadata repository.</summary>
        public StorageRecoveryService(IStorageProvider storage, IRecoveryMetadataRepository metadata, string providerId)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 100) throw new ArgumentException("Invalid provider identity.", nameof(providerId));
            this.storage = storage; this.metadata = metadata; this.providerId = providerId;
        }

        /// <summary>Examines old records and optionally inventories storage. DryRun is the default. All state writes are revision conditional.</summary>
        public async Task<RecoveryResult> RecoverAsync(RecoveryOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            DateTime now = DateTime.UtcNow;
            if (options.MinimumAge <= TimeSpan.Zero || options.MinimumAge.Ticks >= now.Ticks) throw new ArgumentOutOfRangeException(nameof(options.MinimumAge));
            if (options.PageSize < 1 || options.PageSize > 1000 || options.MaximumRecords < 1 || options.MaximumRecords > 100000 ||
                options.MaximumStorageEntries < 1 || options.MaximumStorageEntries > 100000) throw new ArgumentOutOfRangeException(nameof(options));
            // Snapshot mutable settings before the first await.
            var settings = new RecoveryOptions { MinimumAge = options.MinimumAge, DryRun = options.DryRun, PageSize = options.PageSize,
                MaximumRecords = options.MaximumRecords, MaximumStorageEntries = options.MaximumStorageEntries,
                InspectStorage = options.InspectStorage, CleanTemporaries = options.CleanTemporaries };
            DateTime cutoff = now - settings.MinimumAge;
            var result = new RecoveryResult();
            var findings = new List<RecoveryFinding>(); result.Findings = findings;
            var seen = new HashSet<Guid>();
            if (settings.CleanTemporaries) Skip(result, findings, "CleanupRequiresOwnershipEvidence", null);
            int pageNumber = 1;
            int pageBudget = (settings.MaximumRecords + settings.PageSize - 1) / settings.PageSize;
            // All states remain in the query after a transition, so changing status cannot shift offset pagination.
            while (result.RecordsExamined < settings.MaximumRecords)
            {
                PagedResult<FileMetadata> page;
                try
                {
                    page = await metadata.ListAsync(new FileQuery { Status = null, CreatedBeforeUtc = cutoff,
                        PageNumber = pageNumber, PageSize = settings.PageSize }).ConfigureAwait(false);
                }
                catch (Exception error) { Error(result, findings, null, null, error); break; }
                foreach (var record in page.Items)
                {
                    if (result.RecordsExamined >= settings.MaximumRecords) { result.Truncated = true; break; }
                    if (!seen.Add(record.FileId)) continue;
                    result.RecordsExamined++;
                    try { await Examine(record, settings, result, findings).ConfigureAwait(false); }
                    catch (Exception error) { Error(result, findings, record.FileId, null, error); }
                }
                if (page.Items.Count < settings.PageSize || (long)pageNumber * settings.PageSize >= page.TotalCount) break;
                if (result.RecordsExamined >= settings.MaximumRecords) { result.Truncated = true; break; }
                pageNumber++;
                if (pageNumber > pageBudget) { result.Truncated = true; break; }
            }
            if (settings.InspectStorage)
                await Inventory(cutoff, settings, result, findings).ConfigureAwait(false);
            return result;
        }

        private async Task Examine(FileMetadata record, RecoveryOptions options, RecoveryResult result, List<RecoveryFinding> findings)
        {
            if (record.ProviderId != providerId) { Skip(result, findings, "ProviderMismatch", record.FileId); return; }
            bool exists = await storage.ExistsAsync(record.StorageKey).ConfigureAwait(false);
            if (record.Status == FileStatus.Deleted)
            {
                if (exists) Add(findings, "DeletedContentRetained", record.FileId);
                return;
            }
            if (record.Status == FileStatus.Active)
            {
                if (!exists) Add(findings, "ActiveContentMissing", record.FileId);
                return;
            }
            if (record.Status != FileStatus.Pending && record.Status != FileStatus.Failed)
            { Skip(result, findings, "UnknownState", record.FileId); return; }
            if (record.CompletionSha256 == null)
            {
                // Even an old Pending upload may still be writing in another process.
                Skip(result, findings, exists ? "UnverifiableContent" : "UnverifiableMissingContent", record.FileId);
                return;
            }
            bool valid = false;
            if (exists)
            {
                try
                {
                    using (Stream input = await storage.ReadAsync(record.StorageKey).ConfigureAwait(false))
                    using (var counted = new CountingReadStream(input))
                    {
                        try
                        {
                            await counted.CopyToAsync(Stream.Null, 81920).ConfigureAwait(false);
                            valid = counted.BytesRead == record.SizeBytes && counted.CompleteHash() == record.CompletionSha256;
                        }
                        finally { counted.ReleaseHash(); }
                    }
                }
                catch (FileNotFoundException) { valid = false; }
                catch (DirectoryNotFoundException) { valid = false; }
            }
            if (valid)
            {
                if (options.DryRun) { result.ProposedRecoveries++; Add(findings, "WouldRecover", record.FileId); }
                else if (await metadata.TryActivateRecoveredAsync(record).ConfigureAwait(false))
                { result.RecordsRecovered++; Add(findings, "Recovered", record.FileId); }
                else Skip(result, findings, "ConcurrentChange", record.FileId);
            }
            else if (record.Status == FileStatus.Pending)
            {
                if (options.DryRun) { result.ProposedFailures++; Add(findings, "WouldMarkFailed", record.FileId); }
                else
                {
                    // Receipt proves Save completed; an in-flight incomplete writer cannot own this revision.
                    record.Status = FileStatus.Failed;
                    try
                    {
                        await metadata.UpdateAsync(record).ConfigureAwait(false);
                        result.RecordsMarkedFailed++; Add(findings, "MarkedFailed", record.FileId);
                    }
                    catch (InvalidOperationException) { Skip(result, findings, "ConcurrentChange", record.FileId); }
                }
            }
            else Skip(result, findings, "FailedReceiptMismatch", record.FileId);
        }

        private async Task Inventory(DateTime cutoff, RecoveryOptions options, RecoveryResult result, List<RecoveryFinding> findings)
        {
            var diagnostics = storage as IStorageDiagnostics;
            if (diagnostics == null) { Skip(result, findings, "InventoryUnsupported", null); return; }
            try
            {
                StorageInventory inventory = await diagnostics.InspectAsync(options.MaximumStorageEntries).ConfigureAwait(false);
                result.Truncated |= inventory.Truncated;
                result.Errors += inventory.Errors;
                if (inventory.Errors != 0) Add(findings, "InventoryErrors", null);
                if (inventory.LinksSkipped != 0) Skip(result, findings, "InventoryLinksSkipped", null);
                foreach (var entry in inventory.Entries)
                {
                    string reference;
                    using (var hash = SHA256.Create()) reference = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(entry.Key))).Replace("-", "").ToLowerInvariant();
                    if (entry.IsTemporary)
                    {
                        result.TemporariesIdentified++;
                        result.OperationsSkipped++;
                        Add(findings, entry.LastWriteUtc < cutoff ? "OldTemporaryUncertain" : "RecentTemporaryRetained", null, reference);
                        // No cross-process lease links this temporary to a completed/abandoned operation.
                        // Age or an unlocked file cannot prove it is safe to delete.
                        continue;
                    }
                    try
                    {
                        FileMetadata record = await metadata.GetByStorageKeyAsync(providerId, entry.Key).ConfigureAwait(false);
                        if (record == null)
                        {
                            result.OperationsSkipped++;
                            Add(findings, "UnreferencedContentUncertain", null, reference);
                        }
                        else if (record.Status == FileStatus.Deleted) Add(findings, "DeletedContentRetained", record.FileId, reference);
                    }
                    catch (Exception error) { Error(result, findings, null, reference, error); }
                }
            }
            catch (Exception error) { Error(result, findings, null, null, error); }
        }

        private static void Add(List<RecoveryFinding> findings, string code, Guid? id, string reference = null)
        { findings.Add(new RecoveryFinding { Code = code, FileId = id, Reference = reference }); }
        private static void Skip(RecoveryResult result, List<RecoveryFinding> findings, string code, Guid? id)
        { result.OperationsSkipped++; Add(findings, code, id); }
        private static void Error(RecoveryResult result, List<RecoveryFinding> findings, Guid? id, string reference, Exception error)
        {
            result.Errors++; findings.Add(new RecoveryFinding { Code = "Error", FileId = id, Reference = reference, ErrorType = error.GetType().Name });
        }
    }
}
