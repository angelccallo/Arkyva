using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
#if NET472
using System.Data.SqlClient;
#else
using Microsoft.Data.SqlClient;
#endif

namespace Arkyva.Metadata.SqlServer.Repositories
{
    public sealed partial class SqlServerMetadataRepository
    {
        /// <summary>Native synchronous counterpart of AddAsync; shares validation, SQL and parameter construction.</summary>
        public void Add(FileMetadata metadata)
        {
            ValidateAdd(metadata);
            using (var connection = new SqlConnection(connectionString))
            using (var command = CreateAddCommand(connection, metadata))
            {
                Open(connection);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Native synchronous counterpart of GetAsync; shares validation, SQL and parameter construction.</summary>
        public FileMetadata Get(Guid fileId)
        {
            ValidateId(fileId);
            using (var connection = new SqlConnection(connectionString))
            using (var command = CreateGetCommand(connection, fileId))
            {
                Open(connection);
                using (var reader = command.ExecuteReader())
                    return reader.Read() ? Read(reader) : null;
            }
        }

        /// <summary>Native synchronous counterpart of ExistsAsync; shares validation, SQL and parameter construction.</summary>
        public bool Exists(Guid fileId)
        {
            ValidateId(fileId);
            using (var connection = new SqlConnection(connectionString))
            using (var command = CreateExistsCommand(connection, fileId))
            {
                Open(connection);
                return (int)command.ExecuteScalar() == 1;
            }
        }

        /// <summary>Native synchronous counterpart of ListAsync; shares validation, SQL and parameter construction.</summary>
        public PagedResult<FileMetadata> List(FileQuery query)
        {
            Validate(query);
            using (var connection = new SqlConnection(connectionString))
            {
                Open(connection);
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                using (var command = CreateListCommand(connection, transaction, query))
                {
                    var result = new PagedResult<FileMetadata>();
                    var items = new List<FileMetadata>();
                    using (var reader = command.ExecuteReader())
                    {
                        reader.Read();
                        result.TotalCount = reader.GetInt64(0);
                        reader.NextResult();
                        while (reader.Read()) items.Add(Read(reader));
                    }
                    result.Items = items;
                    transaction.Commit();
                    return result;
                }
            }
        }

        /// <summary>Native synchronous counterpart of UpdateAsync; shares validation, SQL and parameter construction.</summary>
        public void Update(FileMetadata metadata)
        {
            Validate(metadata);
            using (var connection = new SqlConnection(connectionString))
            using (var command = CreateUpdateCommand(connection, metadata))
            {
                Open(connection);
                if ((int)command.ExecuteScalar() != 1)
                    throw new InvalidOperationException("Record is missing, revision/immutable fields differ, or the requested state transition is not allowed.");
                metadata.Revision++;
            }
        }

        /// <summary>Native synchronous counterpart of TryRecordUploadAsync; shares validation, SQL and parameter construction.</summary>
        public bool TryRecordUpload(FileMetadata expected, long sizeBytes, string sha256)
        {
            ValidateTryRecordUpload(expected, sizeBytes, sha256);
            using (var connection = new SqlConnection(connectionString))
            using (var command = CreateTryRecordUploadCommand(connection, expected, sizeBytes, sha256))
            {
                Open(connection);
                if ((int)command.ExecuteScalar() != 1) return false;
                expected.Revision++; expected.SizeBytes = sizeBytes; expected.CompletionSha256 = sha256;
                return true;
            }
        }

        /// <summary>Native synchronous counterpart of TryActivateRecoveredAsync; shares validation, SQL and parameter construction.</summary>
        public bool TryActivateRecovered(FileMetadata expected)
        {
            ValidateTryActivateRecovered(expected);
            using (var connection = new SqlConnection(connectionString))
            using (var command = CreateTryActivateRecoveredCommand(connection, expected))
            {
                Open(connection);
                if ((int)command.ExecuteScalar() != 1) return false;
                expected.Revision++; expected.Status = FileStatus.Active;
                return true;
            }
        }

        /// <summary>Native synchronous counterpart of GetByStorageKeyAsync; shares validation, SQL and parameter construction.</summary>
        public FileMetadata GetByStorageKey(string providerId, string storageKey)
        {
            Text(providerId, nameof(providerId), 100, true); Text(storageKey, nameof(storageKey), 1000, true);
            using (var connection = new SqlConnection(connectionString))
            using (var command = CreateGetByStorageKeyCommand(connection, providerId, storageKey))
            {
                Open(connection);
                using (var reader = command.ExecuteReader())
                    return reader.Read() ? Read(reader) : null;
            }
        }

        private static void Open(SqlConnection connection)
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;";
                command.ExecuteNonQuery();
            }
        }
    }
}
