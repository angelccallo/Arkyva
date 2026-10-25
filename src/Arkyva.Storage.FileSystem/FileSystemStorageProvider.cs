using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Arkyva.Core.Models;
using System.Threading.Tasks;
using Arkyva.Core.Interfaces;

namespace Arkyva.Storage.FileSystem
{
    /// <summary>Stores streamed content below a local or UNC directory without replacing existing files.</summary>
    /// <remarks>The repository tree must be protected against external changes to links and directories.</remarks>
    public sealed partial class FileSystemStorageProvider : ISynchronousStorageProvider, IStorageDiagnostics
    {
        private const int BufferSize = 81920;
        private const string TemporaryPrefix = ".arkyva-";
        private readonly string basePath;
        private readonly string basePrefix;

        /// <summary>Validates an absolute local or UNC path and creates the base directory if necessary.</summary>
        /// <param name="basePath">Directory accessible using the application's operating system identity.</param>
        /// <exception cref="ArgumentException">The path is empty, relative or a Windows device path.</exception>
        /// <exception cref="IOException">A path component is a link, or the directory cannot be created.</exception>
        /// <exception cref="UnauthorizedAccessException">The application lacks the required permissions.</exception>
        public FileSystemStorageProvider(string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || !Path.IsPathRooted(basePath) ||
                basePath.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                basePath.StartsWith(@"\\.\", StringComparison.Ordinal) ||
                (Path.DirectorySeparatorChar == '\\' &&
                 !basePath.StartsWith(@"\\", StringComparison.Ordinal) &&
                 !(basePath.Length >= 3 && basePath[1] == ':' && (basePath[2] == '\\' || basePath[2] == '/'))))
                throw new ArgumentException("An absolute local or UNC directory is required.", nameof(basePath));

            this.basePath = Path.GetFullPath(basePath);
            basePrefix = this.basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            RejectLinks(this.basePath);
            Directory.CreateDirectory(this.basePath);
            RejectLinks(this.basePath);
        }

        /// <summary>Copies from the stream's current position, leaving it open. Publishes only after closing the temporary file.</summary>
        /// <exception cref="ArgumentException">The key is invalid or the stream is not readable.</exception>
        /// <exception cref="ArgumentNullException">The stream is null.</exception>
        /// <exception cref="IOException">The destination exists or an I/O operation fails.</exception>
        public async Task SaveAsync(string key, Stream content)
        {
            string destination = PrepareDestination(key, content);
            string directory = Path.GetDirectoryName(destination);
            string temporary = Path.Combine(directory, TemporaryPrefix + Guid.NewGuid().ToString("N") + ".tmp");
            bool ownsTemporary = false;
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    ownsTemporary = true;
                    await content.CopyToAsync(output, BufferSize).ConfigureAwait(false);
                    await output.FlushAsync().ConfigureAwait(false);
                }
                Publish(temporary, destination);
                ownsTemporary = false;
            }
            finally
            {
                if (ownsTemporary)
                {
                    // Cleanup is best effort and must not mask the original failure.
                    CleanupTemporary(temporary);
                }
            }
        }

        /// <summary>Opens a readable asynchronous FileStream. The caller must dispose it.</summary>
        /// <exception cref="FileNotFoundException">The key does not identify an existing file.</exception>
        public Task<Stream> ReadAsync(string key)
        {
            string path = Resolve(key);
            try
            {
                return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan));
            }
            catch (DirectoryNotFoundException error)
            {
                throw new FileNotFoundException("The stored file does not exist.", key, error);
            }
        }

        /// <summary>Returns false only for absent paths or directories. Access and I/O failures propagate.</summary>
        public Task<bool> ExistsAsync(string key)
        {
            string path = Resolve(key);
            try { return Task.FromResult((File.GetAttributes(path) & FileAttributes.Directory) == 0); }
            catch (FileNotFoundException) { return Task.FromResult(false); }
            catch (DirectoryNotFoundException) { return Task.FromResult(false); }
        }

        /// <summary>Physically deletes a file; missing files and parent directories are accepted.</summary>
        public Task DeleteAsync(string key)
        {
            string path = Resolve(key);
            try { File.Delete(path); }
            catch (DirectoryNotFoundException) { }
            return Task.CompletedTask;
        }

        /// <summary>Read-only bounded inventory. Reparse points are skipped and no file is deleted, even when old or apparently unlocked.</summary>
        public Task<StorageInventory> InspectAsync(int maxEntries)
        {
            if (maxEntries < 1 || maxEntries > 100000) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            RejectLinks(basePath);
            var result = new StorageInventory();
            var entries = new List<StorageInventoryEntry>();
            var directories = new Stack<string>(); directories.Push(basePath);
            int visited = 0;
            while (directories.Count > 0 && !result.Truncated)
            {
                string directory = directories.Pop();
                try
                {
                    RejectLinks(directory);
                    foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        if (visited++ >= maxEntries) { result.Truncated = true; break; }
                        try
                        {
                            FileAttributes attributes = File.GetAttributes(path);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { result.LinksSkipped++; continue; }
                            if ((attributes & FileAttributes.Directory) != 0) { directories.Push(path); continue; }
                            RejectLinks(path);
                            entries.Add(new StorageInventoryEntry { Key = path.Substring(basePrefix.Length).Replace('\\', '/'),
                                IsTemporary = Regex.IsMatch(Path.GetFileName(path), @"\A\.arkyva-[0-9a-f]{32}\.tmp\z"),
                                LastWriteUtc = File.GetLastWriteTimeUtc(path) });
                        }
                        catch (IOException) { result.Errors++; }
                        catch (UnauthorizedAccessException) { result.Errors++; }
                    }
                }
                catch (IOException) { result.Errors++; }
                catch (UnauthorizedAccessException) { result.Errors++; }
            }
            result.Entries = entries;
            return Task.FromResult(result);
        }

        private string Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A storage key is required.", nameof(key));
            // A portable, intentionally restricted alphabet avoids ADS, drive paths,
            // device names, separator ambiguity and Windows path normalization aliases.
            string[] segments = key.Replace('\\', '/').Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.StartsWith(TemporaryPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Invalid storage key segment.", nameof(key));
                foreach (char c in segment)
                    if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                        !(c >= '0' && c <= '9') && c != '-' && c != '_' && c != '.')
                        throw new ArgumentException("Unsupported storage key character.", nameof(key));
                string stem = segment.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                     stem[3] >= '0' && stem[3] <= '9'))
                    throw new ArgumentException("Reserved device name.", nameof(key));
            }
            string path = Path.GetFullPath(Path.Combine(basePath, Path.Combine(segments)));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(basePrefix, comparison)) throw new ArgumentException("Key escapes the base directory.", nameof(key));
            RejectLinks(path);
            return path;
        }

        private static void RejectLinks(string path)
        {
            // Include the base directory and its ancestors, not only key components.
            for (string current = path; current != null; current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Symbolic links and reparse points are not supported in storage paths.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
    }
}
