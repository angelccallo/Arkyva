using System;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Services;
using Arkyva.Metadata.SqlServer.Repositories;
using Arkyva.Storage.FileSystem;

namespace Arkyva
{
    /// <summary>Immutable fluent composition of the existing SQL Server and FileSystem providers.</summary>
    /// <remarks>No global configuration, SQL connections, migrations or background work are created. Each fluent call returns a new builder.</remarks>
    public sealed class ArkyvaBuilder
    {
        private readonly string connectionString;
        private readonly string storagePath;

        private ArkyvaBuilder(string connectionString, string storagePath)
        { this.connectionString = connectionString; this.storagePath = storagePath; }

        /// <summary>Starts a new independent configuration.</summary>
        public static ArkyvaBuilder Create() { return new ArkyvaBuilder(null, null); }

        /// <summary>Returns a configuration with a syntactically valid SQL connection string. Does not open SQL.</summary>
        /// <param name="connectionString">Connection string containing Data Source and Initial Catalog; obtain it from secure application configuration.</param>
        /// <exception cref="ArgumentException">Configuration is empty or invalid. InnerException is for trusted diagnostics only.</exception>
        public ArkyvaBuilder UseSqlServer(string connectionString)
        {
            try { new SqlServerMetadataRepository(connectionString); }
            catch (Exception error) when (error is ArgumentException || error is FormatException || error is OverflowException)
            { throw new ArgumentException("Invalid SQL Server configuration. Data Source and Initial Catalog are required.", nameof(connectionString), error); }
            return new ArkyvaBuilder(connectionString, storagePath);
        }

        /// <summary>Returns a configuration with a local or UNC base path. Full path and access validation occur during Build.</summary>
        /// <param name="storagePath">Absolute local directory or Windows UNC path. No directory is created until Build.</param>
        public ArkyvaBuilder UseFileSystem(string storagePath)
        {
            if (string.IsNullOrWhiteSpace(storagePath)) throw new ArgumentException("A storage directory is required.", nameof(storagePath));
            return new ArkyvaBuilder(connectionString, storagePath);
        }

        /// <summary>Constructs independent providers and a reusable service. May create the base directory; never connects to SQL or installs its schema.</summary>
        /// <returns>The existing public service, which owns no long-lived handles and does not require IDisposable.</returns>
        /// <exception cref="InvalidOperationException">A required provider configuration is missing.</exception>
        public IArkyvaService Build()
        {
            // Check all required configuration before creating a directory.
            if (connectionString == null) throw new InvalidOperationException("Call UseSqlServer before Build.");
            if (storagePath == null) throw new InvalidOperationException("Call UseFileSystem before Build.");
            var metadata = new SqlServerMetadataRepository(connectionString);
            var storage = new FileSystemStorageProvider(storagePath);
            return new ArkyvaService(storage, metadata, "FileSystem");
        }
    }
}
