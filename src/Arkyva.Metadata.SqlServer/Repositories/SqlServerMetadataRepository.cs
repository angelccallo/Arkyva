using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using Microsoft.Data.SqlClient;

namespace Arkyva.Metadata.SqlServer.Repositories
{
    /// <summary>Persists technical metadata using short-lived pooled SQL Server connections.</summary>
    /// <remarks>Requires the versioned scripts to be applied separately. Never accesses physical files.</remarks>
    public sealed class SqlServerMetadataRepository : IMetadataRepository
    {
        private readonly string connectionString;
        private const string Columns = "FileId, OriginalName, Extension, MimeType, SizeBytes, ProviderId, StorageKey, Status, CreatedUtc, DeletedUtc";
        private const string Filter = @"
WHERE (@FileId IS NULL OR FileId = @FileId)
  AND (@Status IS NULL OR Status = @Status)
  AND (@OriginalName IS NULL OR (OriginalName = @OriginalName COLLATE Latin1_General_100_BIN2 AND DATALENGTH(OriginalName) = DATALENGTH(@OriginalName)))
  AND (@Extension IS NULL OR (Extension = @Extension COLLATE Latin1_General_100_BIN2 AND DATALENGTH(Extension) = DATALENGTH(@Extension)))
  AND (@CreatedFrom IS NULL OR CreatedUtc >= @CreatedFrom)
  AND (@CreatedBefore IS NULL OR CreatedUtc < @CreatedBefore)";

        /// <summary>Validates configuration without opening a connection or applying migrations.</summary>
        /// <param name="connectionString">SQL Server connection string; credentials are never written to logs.</param>
        public SqlServerMetadataRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("A connection string is required.", nameof(connectionString));
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog))
                throw new ArgumentException("Data Source and Initial Catalog are required.", nameof(connectionString));
            this.connectionString = builder.ConnectionString;
        }

        /// <summary>Inserts metadata without replacing an existing identifier or provider/key pair.</summary>
        /// <exception cref="ArgumentException">Metadata violates the documented field or UTC requirements.</exception>
        /// <exception cref="SqlException">SQL Server rejects the insert, including duplicate keys.</exception>
        public async Task AddAsync(FileMetadata metadata)
        {
            Validate(metadata);
            using (var connection = new SqlConnection(connectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"INSERT INTO arkyva.Files
(FileId, OriginalName, Extension, MimeType, SizeBytes, ProviderId, StorageKey, Status, CreatedUtc, DeletedUtc)
VALUES (@FileId, @OriginalName, @Extension, @MimeType, @SizeBytes, @ProviderId, @StorageKey, @Status, @CreatedUtc, @DeletedUtc);";
                MetadataParameters(command, metadata);
                await OpenAsync(connection).ConfigureAwait(false);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Returns a record in any state, or null if absent.</summary>
        public async Task<FileMetadata> GetAsync(Guid fileId)
        {
            ValidateId(fileId);
            using (var connection = new SqlConnection(connectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT " + Columns + " FROM arkyva.Files WHERE FileId = @FileId;";
                Parameter(command, "@FileId", SqlDbType.UniqueIdentifier, fileId);
                await OpenAsync(connection).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                    return await reader.ReadAsync().ConfigureAwait(false) ? Read(reader) : null;
            }
        }

        /// <summary>Returns true only for an Active record, preserving the Core contract.</summary>
        public async Task<bool> ExistsAsync(Guid fileId)
        {
            ValidateId(fileId);
            using (var connection = new SqlConnection(connectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM arkyva.Files WHERE FileId = @FileId AND Status = 'Active') THEN 1 ELSE 0 END;";
                Parameter(command, "@FileId", SqlDbType.UniqueIdentifier, fileId);
                await OpenAsync(connection).ConfigureAwait(false);
                return (int)await command.ExecuteScalarAsync().ConfigureAwait(false) == 1;
            }
        }

        /// <summary>Lists a bounded page with exact filters, ordered by CreatedUtc and FileId ascending.</summary>
        /// <remarks>Count and page share a short serializable transaction for a consistent result.</remarks>
        public async Task<PagedResult<FileMetadata>> ListAsync(FileQuery query)
        {
            Validate(query);
            using (var connection = new SqlConnection(connectionString))
            {
                await OpenAsync(connection).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT COUNT_BIG(*) FROM arkyva.Files " + Filter + "; SELECT " + Columns +
                        " FROM arkyva.Files " + Filter + " ORDER BY CreatedUtc, FileId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
                    Parameter(command, "@FileId", SqlDbType.UniqueIdentifier, query.FileId);
                    Parameter(command, "@Status", SqlDbType.VarChar, query.Status.HasValue ? query.Status.Value.ToString() : null, 20);
                    Parameter(command, "@OriginalName", SqlDbType.NVarChar, query.OriginalName, 255);
                    Parameter(command, "@Extension", SqlDbType.NVarChar, query.Extension, 30);
                    Parameter(command, "@CreatedFrom", SqlDbType.DateTime2, query.CreatedFromUtc);
                    Parameter(command, "@CreatedBefore", SqlDbType.DateTime2, query.CreatedBeforeUtc);
                    Parameter(command, "@Offset", SqlDbType.BigInt, ((long)query.PageNumber - 1) * query.PageSize);
                    Parameter(command, "@PageSize", SqlDbType.Int, query.PageSize);
                    var result = new PagedResult<FileMetadata>();
                    var items = new List<FileMetadata>();
                    using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        await reader.ReadAsync().ConfigureAwait(false);
                        result.TotalCount = reader.GetInt64(0);
                        await reader.NextResultAsync().ConfigureAwait(false);
                        while (await reader.ReadAsync().ConfigureAwait(false)) items.Add(Read(reader));
                    }
                    result.Items = items;
                    transaction.Commit();
                    return result;
                }
            }
        }

        /// <summary>Updates descriptive metadata and state while preserving identity, physical reference and creation time; size can be finalized only during Pending to Active.</summary>
        /// <remarks>Pending may become Active, Failed or Deleted; Active may become Failed or Deleted; Failed may become Deleted. Deleted is terminal. Same-state updates are allowed.</remarks>
        /// <exception cref="InvalidOperationException">The record is missing, a transition is forbidden, or immutable values conflict.</exception>
        public async Task UpdateAsync(FileMetadata metadata)
        {
            Validate(metadata);
            using (var connection = new SqlConnection(connectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"UPDATE arkyva.Files SET
OriginalName = @OriginalName, Extension = @Extension, MimeType = @MimeType,
Status = @Status, SizeBytes = @SizeBytes, DeletedUtc = @DeletedUtc, UpdatedUtc = SYSUTCDATETIME()
WHERE FileId = @FileId AND ProviderId = @ProviderId COLLATE Latin1_General_100_BIN2
AND DATALENGTH(ProviderId) = DATALENGTH(@ProviderId)
AND StorageKey = @StorageKey COLLATE Latin1_General_100_BIN2 AND DATALENGTH(StorageKey) = DATALENGTH(@StorageKey)
AND (SizeBytes = @SizeBytes OR (Status = 'Pending' AND @Status = 'Active')) AND CreatedUtc = @CreatedUtc
AND ((Status = @Status AND (DeletedUtc = @DeletedUtc OR (DeletedUtc IS NULL AND @DeletedUtc IS NULL)))
 OR (Status = 'Pending' AND @Status IN ('Active', 'Failed', 'Deleted'))
 OR (Status = 'Active' AND @Status IN ('Failed', 'Deleted'))
 OR (Status = 'Failed' AND @Status = 'Deleted'));
SELECT @@ROWCOUNT;";
                MetadataParameters(command, metadata);
                await OpenAsync(connection).ConfigureAwait(false);
                if ((int)await command.ExecuteScalarAsync().ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Record is missing, immutable fields differ, or the requested state transition is not allowed.");
            }
        }

        private static async Task OpenAsync(SqlConnection connection)
        {
            await connection.OpenAsync().ConfigureAwait(false);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static FileMetadata Read(SqlDataReader reader)
        {
            return new FileMetadata
            {
                FileId = reader.GetGuid(0), OriginalName = reader.GetString(1),
                Extension = reader.IsDBNull(2) ? null : reader.GetString(2),
                MimeType = reader.IsDBNull(3) ? null : reader.GetString(3), SizeBytes = reader.GetInt64(4),
                ProviderId = reader.GetString(5), StorageKey = reader.GetString(6),
                Status = (FileStatus)Enum.Parse(typeof(FileStatus), reader.GetString(7)),
                CreatedUtc = DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc),
                DeletedUtc = reader.IsDBNull(9) ? (DateTime?)null : DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc)
            };
        }

        private static void MetadataParameters(SqlCommand command, FileMetadata metadata)
        {
            Parameter(command, "@FileId", SqlDbType.UniqueIdentifier, metadata.FileId);
            Parameter(command, "@OriginalName", SqlDbType.NVarChar, metadata.OriginalName, 255);
            Parameter(command, "@Extension", SqlDbType.NVarChar, metadata.Extension, 30);
            Parameter(command, "@MimeType", SqlDbType.NVarChar, metadata.MimeType, 255);
            Parameter(command, "@SizeBytes", SqlDbType.BigInt, metadata.SizeBytes);
            Parameter(command, "@ProviderId", SqlDbType.NVarChar, metadata.ProviderId, 100);
            Parameter(command, "@StorageKey", SqlDbType.NVarChar, metadata.StorageKey, 1000);
            Parameter(command, "@Status", SqlDbType.VarChar, metadata.Status.ToString(), 20);
            Parameter(command, "@CreatedUtc", SqlDbType.DateTime2, metadata.CreatedUtc);
            Parameter(command, "@DeletedUtc", SqlDbType.DateTime2, metadata.DeletedUtc);
        }

        private static void Parameter(SqlCommand command, string name, SqlDbType type, object value, int size = 0)
        {
            var parameter = command.Parameters.Add(name, type);
            if (size != 0) parameter.Size = size;
            if (type == SqlDbType.DateTime2) parameter.Scale = 7;
            parameter.Value = value ?? DBNull.Value;
        }

        private static void ValidateId(Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("FileId must not be empty.", nameof(id));
        }

        private static void Text(string value, string name, int maximum, bool required)
        {
            if ((required && string.IsNullOrWhiteSpace(value)) || (value != null && value.Length > maximum))
                throw new ArgumentException("Invalid or overlong " + name + ".", name);
        }

        private static void Utc(DateTime value, string name)
        {
            if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC DateTime required.", name);
        }

        private static void Validate(FileMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            ValidateId(metadata.FileId);
            Text(metadata.OriginalName, nameof(metadata.OriginalName), 255, true);
            Text(metadata.Extension, nameof(metadata.Extension), 30, false);
            Text(metadata.MimeType, nameof(metadata.MimeType), 255, false);
            Text(metadata.ProviderId, nameof(metadata.ProviderId), 100, true);
            Text(metadata.StorageKey, nameof(metadata.StorageKey), 1000, true);
            if (metadata.SizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(metadata.SizeBytes));
            if (!Enum.IsDefined(typeof(FileStatus), metadata.Status)) throw new ArgumentOutOfRangeException(nameof(metadata.Status));
            Utc(metadata.CreatedUtc, nameof(metadata.CreatedUtc));
            if (metadata.DeletedUtc.HasValue)
            {
                Utc(metadata.DeletedUtc.Value, nameof(metadata.DeletedUtc));
                if (metadata.DeletedUtc < metadata.CreatedUtc) throw new ArgumentException("Deletion precedes creation.", nameof(metadata));
            }
            if ((metadata.Status == FileStatus.Deleted) != metadata.DeletedUtc.HasValue)
                throw new ArgumentException("Deleted status requires DeletedUtc; other states forbid it.", nameof(metadata));
        }

        private static void Validate(FileQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.PageNumber <= 0) throw new ArgumentOutOfRangeException(nameof(query.PageNumber));
            if (query.PageSize <= 0 || query.PageSize > 1000) throw new ArgumentOutOfRangeException(nameof(query.PageSize), "PageSize must be between 1 and 1000.");
            if (query.FileId.HasValue) ValidateId(query.FileId.Value);
            if (query.Status.HasValue && !Enum.IsDefined(typeof(FileStatus), query.Status.Value)) throw new ArgumentOutOfRangeException(nameof(query.Status));
            Text(query.OriginalName, nameof(query.OriginalName), 255, false);
            Text(query.Extension, nameof(query.Extension), 30, false);
            if (query.CreatedFromUtc.HasValue) Utc(query.CreatedFromUtc.Value, nameof(query.CreatedFromUtc));
            if (query.CreatedBeforeUtc.HasValue) Utc(query.CreatedBeforeUtc.Value, nameof(query.CreatedBeforeUtc));
            if (query.CreatedFromUtc.HasValue && query.CreatedBeforeUtc.HasValue && query.CreatedFromUtc >= query.CreatedBeforeUtc)
                throw new ArgumentException("Creation interval must be non-empty.", nameof(query));
        }
    }
}
