using System;
using System.IO;

namespace Arkyva.Storage.FileSystem
{
    public sealed partial class FileSystemStorageProvider
    {
        /// <summary>Uses synchronous streaming, an exclusive same-directory temporary and no-overwrite publication. Leaves content open.</summary>
        public void Save(string key, Stream content)
        {
            string destination = PrepareDestination(key, content);
            string directory = Path.GetDirectoryName(destination);
            string temporary = Path.Combine(directory, TemporaryPrefix + Guid.NewGuid().ToString("N") + ".tmp");
            bool ownsTemporary = false;
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, BufferSize, FileOptions.SequentialScan))
                {
                    ownsTemporary = true;
                    content.CopyTo(output, BufferSize);
                    output.Flush();
                }
                Publish(temporary, destination);
                ownsTemporary = false;
            }
            finally { if (ownsTemporary) CleanupTemporary(temporary); }
        }

        /// <summary>Opens a readable synchronous FileStream at the start. The consumer owns the stream.</summary>
        public Stream Read(string key)
        {
            string path = Resolve(key);
            try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan); }
            catch (DirectoryNotFoundException error) { throw new FileNotFoundException("The stored file does not exist.", key, error); }
        }

        /// <summary>Returns false only for absent content/directories; access and I/O failures propagate.</summary>
        public bool Exists(string key)
        {
            string path = Resolve(key);
            try { return (File.GetAttributes(path) & FileAttributes.Directory) == 0; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        /// <summary>Deletes physically; missing files and parent directories are accepted.</summary>
        public void Delete(string key)
        {
            string path = Resolve(key);
            try { File.Delete(path); }
            catch (DirectoryNotFoundException) { }
        }

        private string PrepareDestination(string key, Stream content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (!content.CanRead) throw new ArgumentException("A readable stream is required.", nameof(content));
            string destination = Resolve(key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            RejectLinks(destination);
            return destination;
        }

        private void Publish(string temporary, string destination)
        {
            RejectLinks(destination);
            // Same-directory rename; the filesystem arbitrates conflicts across processes.
            File.Move(temporary, destination);
        }

        private static void CleanupTemporary(string temporary)
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
