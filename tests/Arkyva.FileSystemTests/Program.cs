using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arkyva.Storage.FileSystem;

internal static class Program
{
    private static int passed;
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "worker")
        {
            try
            {
                var provider = new FileSystemStorageProvider(args[1]);
                File.WriteAllText(args[2] + "." + args[3], "ready");
                var timer = Stopwatch.StartNew();
                while (!File.Exists(args[2]))
                {
                    if (timer.ElapsedMilliseconds > 10000) return 3;
                    Thread.Sleep(10);
                }
                using (var input = new GeneratedStream(1024 * 1024))
                    provider.SaveAsync("process-contention.bin", input).GetAwaiter().GetResult();
                return 0;
            }
            catch (IOException) { return 2; }
            catch (Exception error) { Console.Error.WriteLine(error); return 3; }
        }
        try { Run().GetAwaiter().GetResult(); Console.WriteLine(passed + " filesystem tests passed."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "Arkyva-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string storage = Path.Combine(root, "files");
            var provider = new FileSystemStorageProvider(storage);
            await SynchronousChecks(provider, storage);
            Check(Directory.Exists(storage), "Creates base directory");
            foreach (string path in new[] { null, "", " ", "relative", @"C:relative", @"\rooted", @"\\?\C:\files" })
                await Throws<ArgumentException>(() => Task.FromResult(new FileSystemStorageProvider(path)));
            string occupied = Path.Combine(root, "occupied");
            File.WriteAllText(occupied, "file");
            await Throws<IOException>(() => Task.FromResult(new FileSystemStorageProvider(occupied)));
            Pass("Rejects invalid configuration and file as base");

            byte[] expected = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            using (var input = new MemoryStream(expected))
            {
                await provider.SaveAsync("2026/10/unique.bin", input);
                Check(input.CanRead, "Input stream remains open");
            }
            using (Stream read = await provider.ReadAsync("2026/10/unique.bin"))
            using (var copy = new MemoryStream())
            {
                Check(read is FileStream && read.Position == 0, "Read returns a FileStream at position zero");
                await read.CopyToAsync(copy);
                Check(expected.SequenceEqual(copy.ToArray()), "Roundtrip content integrity");
            }
            Check(await provider.ExistsAsync("2026/10/unique.bin") && !await provider.ExistsAsync("absent/deep.bin"), "Existing and missing files");
            await Throws<FileNotFoundException>(() => provider.ReadAsync("absent/deep.bin"));
            await Throws<FileNotFoundException>(() => provider.ReadAsync("2026/10/missing.bin"));
            Pass("Missing reads report FileNotFoundException");
            await provider.DeleteAsync("2026/10/unique.bin");
            await provider.DeleteAsync("2026/10/unique.bin");
            await provider.DeleteAsync("absent/deep.bin");
            Check(!await provider.ExistsAsync("2026/10/unique.bin"), "Physical and idempotent deletion");

            string[] invalid = { null, "", " ", "../escape", "a/../../escape", "/root", @"C:\escape", @"\\server\share\file", @"a\..\escape", "a//b", "a/./b", "a/", "a:b", "a?b", "a*b", "a\0b", "a. ", "a.", "CON", "nul.txt", "COM1.bin", "LPT9", ".arkyva-secret.tmp", "folder/.arkyva-secret.tmp", "a/%2e%2e/b" };
            foreach (string key in invalid)
            {
                await Throws<ArgumentException>(() => provider.SaveAsync(key, new MemoryStream()));
                await Throws<ArgumentException>(() => provider.ReadAsync(key));
                await Throws<ArgumentException>(() => provider.ExistsAsync(key));
                await Throws<ArgumentException>(() => provider.DeleteAsync(key));
            }
            Check(!File.Exists(Path.Combine(root, "escape")), "Dangerous keys rejected by all operations");
            await Throws<ArgumentNullException>(() => provider.SaveAsync("null.bin", null));
            using (var disposed = new MemoryStream())
            {
                disposed.Dispose();
                await Throws<ArgumentException>(() => provider.SaveAsync("disposed.bin", disposed));
            }
            using (var writeOnly = new FileStream(Path.Combine(root, "write-only"), FileMode.Create, FileAccess.Write))
                await Throws<ArgumentException>(() => provider.SaveAsync("unreadable.bin", writeOnly));
            Pass("Null, closed and unreadable streams rejected");

            using (var input = new MemoryStream(new byte[] { 0, 1, 2, 3 }))
            {
                input.Position = 2;
                await provider.SaveAsync(@"nested\position.bin", input);
            }
            Check(File.ReadAllBytes(Path.Combine(storage, "nested", "position.bin")).SequenceEqual(new byte[] { 2, 3 }), "Uses current position and supports relative backslashes");
            await Throws<IOException>(() => provider.SaveAsync("nested/position.bin", new MemoryStream(new byte[] { 9 })));
            Check(File.ReadAllBytes(Path.Combine(storage, "nested", "position.bin")).SequenceEqual(new byte[] { 2, 3 }), "Existing content never overwritten");

            const long largeSize = 32L * 1024 * 1024 + 17;
            using (var input = new GeneratedStream(largeSize)) await provider.SaveAsync("large.bin", input);
            using (Stream read = await provider.ReadAsync("large.bin"))
            {
                byte[] buffer = new byte[81920]; long position = 0; int count;
                while ((count = await read.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < count; i++)
                        if (buffer[i] != (byte)((position + i) % 251)) throw new Exception("Large content mismatch");
                    position += count;
                }
                Check(position == largeSize, "Large non-seekable stream has exact content and length");
            }

            using (var failure = new GeneratedStream(200000, true))
                await Throws<IOException>(() => provider.SaveAsync("failure/file.bin", failure));
            Check(!await provider.ExistsAsync("failure/file.bin") && NoTemps(storage), "Write failure cleans temporary and never publishes");

            using (var blocked = new GatedStream())
            {
                Task saving = provider.SaveAsync("visibility.bin", blocked);
                await WithTimeout(blocked.Entered.Task);
                try
                {
                    Check(!await provider.ExistsAsync("visibility.bin"), "Final file invisible during incomplete write");
                    await Throws<FileNotFoundException>(() => provider.ReadAsync("visibility.bin"));
                }
                finally { blocked.Release.TrySetResult(true); }
                await saving;
                Check(await provider.ExistsAsync("visibility.bin"), "Final file visible after publication");
            }

            await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
            {
                var independent = new FileSystemStorageProvider(storage);
                using (var input = new MemoryStream(new[] { (byte)i })) await independent.SaveAsync("parallel/" + i + ".bin", input);
            })));
            Check(Enumerable.Range(0, 12).All(i => File.ReadAllBytes(Path.Combine(storage, "parallel", i + ".bin")).SequenceEqual(new[] { (byte)i })), "Concurrent independent keys");

            using (var first = new GatedStream())
            using (var second = new GatedStream())
            {
                Task a = provider.SaveAsync("contended.bin", first);
                Task b = new FileSystemStorageProvider(storage).SaveAsync("contended.bin", second);
                await WithTimeout(Task.WhenAll(first.Entered.Task, second.Entered.Task));
                first.Release.TrySetResult(true); second.Release.TrySetResult(true);
                int successes = 0, conflicts = 0;
                foreach (Task task in new[] { a, b })
                    try { await task; successes++; } catch (IOException) { conflicts++; }
                Check(successes == 1 && conflicts == 1 && new System.IO.FileInfo(Path.Combine(storage, "contended.bin")).Length == 1 && NoTemps(storage), "Same-key writers produce one complete winner and one conflict");
            }

            string gate = Path.Combine(root, "process-gate");
            string executable = typeof(Program).Assembly.Location;
            using (Process first = StartWorker(executable, storage, gate, "one"))
            using (Process second = StartWorker(executable, storage, gate, "two"))
            {
                try
                {
                    var timer = Stopwatch.StartNew();
                    while (!File.Exists(gate + ".one") || !File.Exists(gate + ".two"))
                    {
                        if (timer.ElapsedMilliseconds > 10000 || first.HasExited || second.HasExited)
                            throw new Exception("Workers did not reach the start barrier");
                        await Task.Delay(10);
                    }
                    File.WriteAllText(gate, "go");
                    if (!first.WaitForExit(10000) || !second.WaitForExit(10000)) throw new TimeoutException("Worker did not exit");
                    Check(new[] { first.ExitCode, second.ExitCode }.OrderBy(x => x).SequenceEqual(new[] { 0, 2 }) &&
                        new System.IO.FileInfo(Path.Combine(storage, "process-contention.bin")).Length == 1024 * 1024 && NoTemps(storage),
                        "Separate processes cannot overwrite the same key");
                }
                finally
                {
                    foreach (Process process in new[] { first, second })
                        if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
                }
            }

            Directory.CreateDirectory(Path.Combine(storage, "directory.bin"));
            await Throws<IOException>(() => provider.SaveAsync("directory.bin", new MemoryStream(new byte[] { 1 })));
            Check(NoTemps(storage), "Publication failure cleans temporary");
            using (var locked = new FileStream(Path.Combine(storage, "locked.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                await Throws<IOException>(() => provider.ReadAsync("locked.bin"));
                await Throws<IOException>(() => provider.DeleteAsync("locked.bin"));
            }
            Pass("Sharing violations propagate");
        }
        finally
        {
            // Only the unique absolute directory created by this test run may be removed.
            string full = Path.GetFullPath(root);
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Arkyva-tests-", StringComparison.Ordinal))
                throw new Exception("Unsafe cleanup target");
            Directory.Delete(full, true);
        }
    }

    private static Process StartWorker(string executable, string storage, string gate, string id)
    {
        return Process.Start(new ProcessStartInfo(executable,
            "worker \"" + storage + "\" \"" + gate + "\" " + id)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static async Task SynchronousChecks(FileSystemStorageProvider provider, string storage)
    {
        using (var input = new MemoryStream(new byte[] { 1, 2, 3 }))
        {
            provider.Save("sync/content.bin", input);
            Check(input.CanRead && provider.Exists("sync/content.bin"), "Sync Save preserves input ownership and creates subdirectories");
        }
        using (var content = (FileStream)provider.Read("sync/content.bin"))
            Check(!content.IsAsync && content.Position == 0 && content.ReadByte() == 1, "Sync Read is a native FileStream");
        foreach (string key in new[] { null, "", "../x", @"..\x", "/root", @"C:\root", "a//b", "CON", ".arkyva-x.tmp" })
        {
            ThrowsSync<ArgumentException>(() => provider.Save(key, new MemoryStream()));
            ThrowsSync<ArgumentException>(() => provider.Read(key));
            ThrowsSync<ArgumentException>(() => provider.Exists(key));
            ThrowsSync<ArgumentException>(() => provider.Delete(key));
        }
        Pass("Sync methods reject traversal, absolute paths and reserved keys");
        ThrowsSync<ArgumentNullException>(() => provider.Save("sync/null", null));
        var closed = new MemoryStream(); closed.Dispose();
        ThrowsSync<ArgumentException>(() => provider.Save("sync/closed", closed));
        ThrowsSync<IOException>(() => provider.Save("sync/content.bin", new MemoryStream(new byte[] { 9 })));
        using (var content = provider.Read("sync/content.bin")) Check(content.ReadByte() == 1, "Sync Save never overwrites");
        using (var input = new GeneratedStream(160000, true)) ThrowsSync<IOException>(() => provider.Save("sync/failure.bin", input));
        Check(!provider.Exists("sync/failure.bin") && NoTemps(storage), "Sync write error cleans temporary without publishing");
        Directory.CreateDirectory(Path.Combine(storage, "sync", "directory.bin"));
        ThrowsSync<IOException>(() => provider.Save("sync/directory.bin", new MemoryStream(new byte[] { 1 })));
        Check(NoTemps(storage), "Sync publication conflict cleans temporary");
        using (var gated = new NativeGatedStream())
        {
            Task write = Task.Run(() => provider.Save("sync/inflight.bin", gated));
            try
            {
                await WithTimeout(gated.Entered.Task);
                Check(!provider.Exists("sync/inflight.bin"), "Sync final path is invisible during incomplete writes");
            }
            finally { gated.Release.Set(); }
            await write;
            Check(provider.Exists("sync/inflight.bin") && NoTemps(storage), "Sync final path appears only after completed publication");
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            try
            {
                using (var input = new MemoryStream(new byte[] { (byte)i }))
                    if (i % 2 == 0) await Task.Run(() => provider.Save("sync/race.bin", input));
                    else await provider.SaveAsync("sync/race.bin", input);
                return true;
            }
            catch (IOException) { return false; }
        }));
        Check(outcomes.Count(x => x) == 1 && new System.IO.FileInfo(Path.Combine(storage, "sync", "race.bin")).Length == 1 && NoTemps(storage), "Mixed sync/async same-key writers have one complete winner");
        provider.Delete("sync/content.bin"); provider.Delete("sync/content.bin");
        Check(!provider.Exists("sync/content.bin"), "Sync physical deletion is idempotent");
        ThrowsSync<FileNotFoundException>(() => provider.Read("sync/content.bin"));
    }
    private static void ThrowsSync<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class NativeGatedStream : GeneratedStream
    {
        internal readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
        internal NativeGatedStream() : base(1) { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Entered.TrySetResult(true);
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Native stream gate timeout");
            return base.Read(buffer, offset, count);
        }
        protected override void Dispose(bool disposing) { if (disposing) Release.Dispose(); base.Dispose(disposing); }
    }
    private static bool NoTemps(string root) { return Directory.GetFiles(root, ".arkyva-*.tmp", SearchOption.AllDirectories).Length == 0; }
    private static async Task WithTimeout(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(10000)) != task) throw new TimeoutException("Stream barrier was not reached");
        await task;
    }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Pass(message); }
    private static void Pass(string message) { passed++; Console.WriteLine("PASS: " + message); }

    private class GeneratedStream : Stream
    {
        private readonly long size;
        private readonly bool fail;
        private long position;
        public GeneratedStream(long size, bool fail = false) { this.size = size; this.fail = fail; }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (fail && position > 0) throw new IOException("Injected input failure");
            int length = (int)Math.Min(count, size - position);
            for (int i = 0; i < length; i++) buffer[offset + i] = (byte)((position + i) % 251);
            position += length;
            return length;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { return Task.FromResult(Read(buffer, offset, count)); }
        public override void Flush() { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    private sealed class GatedStream : GeneratedStream
    {
        public readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public GatedStream() : base(1) { }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Release.Task.ConfigureAwait(false);
            return await base.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }
    }
}
