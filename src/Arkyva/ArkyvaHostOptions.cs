using System;
using Arkyva.Core.Interfaces;

namespace Arkyva
{
    /// <summary>Callback-scoped configuration that delegates to the existing immutable ArkyvaBuilder.</summary>
    public sealed class ArkyvaHostOptions
    {
        private readonly object gate = new object();
        private ArkyvaBuilder builder = ArkyvaBuilder.Create();

        internal ArkyvaHostOptions() { }

        /// <summary>Configures SQL Server using the builder's existing validation, without opening a connection.</summary>
        /// <param name="connectionString">Application-supplied SQL Server connection string.</param>
        /// <returns>The same options object for optional chaining.</returns>
        public ArkyvaHostOptions UseSqlServer(string connectionString)
        {
            lock (gate) { EnsureOpen(); builder = builder.UseSqlServer(connectionString); }
            return this;
        }

        /// <summary>Configures the local or UNC base directory, validated by Build after the callback.</summary>
        /// <param name="storagePath">Absolute storage directory accessible by the application's identity.</param>
        /// <returns>The same options object for optional chaining.</returns>
        public ArkyvaHostOptions UseFileSystem(string storagePath)
        {
            lock (gate) { EnsureOpen(); builder = builder.UseFileSystem(storagePath); }
            return this;
        }

        internal IArkyvaService Build()
        {
            ArkyvaBuilder configured;
            lock (gate) { EnsureOpen(); configured = builder; builder = null; }
            return configured.Build();
        }

        internal void Close() { lock (gate) { builder = null; } }

        private void EnsureOpen()
        {
            if (builder == null)
                throw new InvalidOperationException("ArkyvaHost options may only be used during the configuration callback.");
        }
    }
}
