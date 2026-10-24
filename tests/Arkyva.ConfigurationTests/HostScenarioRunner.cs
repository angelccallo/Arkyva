using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arkyva;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Services;

// Each scenario runs in a fresh AppDomain; no production reset hook or private-state mutation.
public sealed class HostScenarioRunner : MarshalByRefObject
{
    private const string Connection = "Data Source=invalid.example;Initial Catalog=host_validation;Integrated Security=True;Connect Timeout=1";

    public void Run(string scenario, string root)
    {
        switch (scenario)
        {
            case "Current before configuration":
                Check(Throws<InvalidOperationException>(() => { var unused = ArkyvaHost.Current; }).Message.Contains("Configure"));
                break;
            case "Valid callback builds once with separate Use calls":
                Configure(root);
                Check(ArkyvaHost.Current is ArkyvaService && Directory.Exists(root));
                Check(!(ArkyvaHost.Current is IDisposable));
                Check(ReferenceEquals(ArkyvaHost.Current, ArkyvaHost.Current));
                break;
            case "Chained options and reverse order":
                ArkyvaHost.Configure(o => o.UseFileSystem(root).UseSqlServer(Connection));
                Check(ArkyvaHost.Current != null);
                break;
            case "Null callback does not configure":
                Throws<ArgumentNullException>(() => ArkyvaHost.Configure(null));
                Throws<InvalidOperationException>(() => { var unused = ArkyvaHost.Current; });
                Configure(root);
                break;
            case "Duplicate configuration preserves original without invoking callback":
                Configure(root);
                var original = ArkyvaHost.Current;
                bool invoked = false;
                Throws<InvalidOperationException>(() => ArkyvaHost.Configure(o => { invoked = true; }));
                Check(!invoked && ReferenceEquals(original, ArkyvaHost.Current));
                break;
            case "Failed Build permits retry with corrected filesystem":
                File.WriteAllText(root, "occupied");
                Throws<IOException>(() => Configure(root));
                Throws<InvalidOperationException>(() => { var unused = ArkyvaHost.Current; });
                File.Delete(root);
                Configure(root);
                Check(Directory.Exists(root) && ArkyvaHost.Current != null);
                break;
            case "Missing options permits retry without directory side effect":
                Throws<InvalidOperationException>(() => ArkyvaHost.Configure(o => o.UseFileSystem(root)));
                Check(!Directory.Exists(root));
                Configure(root);
                break;
            case "Callback failure preserves original exception and permits retry":
                var cause = new InvalidOperationException("callback failure");
                Check(ReferenceEquals(cause, Throws<InvalidOperationException>(() => ArkyvaHost.Configure(o => { throw cause; }))));
                Configure(root);
                break;
            case "Concurrent Configure has one callback and one published instance":
                ConcurrentConfiguration(root);
                break;
            case "Concurrent Current always returns same service":
                Configure(root);
                var service = ArkyvaHost.Current;
                Parallel.For(0, 128, i => Check(ReferenceEquals(service, ArkyvaHost.Current)));
                break;
            case "Recursive Configure is rejected and retry remains possible":
                Throws<InvalidOperationException>(() => ArkyvaHost.Configure(o => Configure(root)));
                Throws<InvalidOperationException>(() => { var unused = ArkyvaHost.Current; });
                Configure(root);
                break;
            case "Captured options cannot mutate configured host":
                ArkyvaHostOptions captured = null;
                ArkyvaHost.Configure(o => { captured = o; o.UseSqlServer(Connection); o.UseFileSystem(root); });
                var retained = ArkyvaHost.Current;
                Throws<InvalidOperationException>(() => captured.UseFileSystem(root + "-other"));
                Check(!Directory.Exists(root + "-other") && ReferenceEquals(retained, ArkyvaHost.Current));
                break;
            case "Failed callback closes captured options":
                ArkyvaHostOptions failed = null;
                Throws<InvalidOperationException>(() => ArkyvaHost.Configure(o => { failed = o; throw new InvalidOperationException(); }));
                Throws<InvalidOperationException>(() => failed.UseSqlServer(Connection));
                Configure(root);
                break;
            case "Traditional builder remains independent before and after host":
                var builder = ArkyvaBuilder.Create().UseSqlServer(Connection).UseFileSystem(root);
                IArkyvaService first = builder.Build();
                Throws<InvalidOperationException>(() => { var unused = ArkyvaHost.Current; });
                Configure(root);
                Check(!ReferenceEquals(first, ArkyvaHost.Current));
                Check(!ReferenceEquals(builder.Build(), ArkyvaHost.Current));
                break;
            default: throw new InvalidOperationException("Unknown scenario");
        }
    }

    private static void Configure(string root)
    {
        ArkyvaHost.Configure(o => { o.UseSqlServer(Connection); o.UseFileSystem(root); });
    }

    private static void ConcurrentConfiguration(string root)
    {
        using (var start = new Barrier(2))
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            int callbacks = 0;
            var attempts = Enumerable.Range(0, 2).Select(i => Task.Run(() =>
            {
                if (!start.SignalAndWait(TimeSpan.FromSeconds(10))) throw new Exception("Start timeout");
                try
                {
                    ArkyvaHost.Configure(o =>
                    {
                        Interlocked.Increment(ref callbacks);
                        entered.Set();
                        if (!release.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Release timeout");
                        o.UseSqlServer(Connection);
                        o.UseFileSystem(Path.Combine(root, i.ToString()));
                    });
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            })).ToArray();
            try
            {
                Check(entered.Wait(TimeSpan.FromSeconds(10)));
                // No service is observable before the winning callback and Build finish.
                Throws<InvalidOperationException>(() => { var unused = ArkyvaHost.Current; });
            }
            finally { release.Set(); }
            Check(Task.WaitAll(attempts, TimeSpan.FromSeconds(15)));
            Check(callbacks == 1 && attempts.Count(t => t.Result) == 1);
            Check(Directory.GetDirectories(root).Length == 1 && ArkyvaHost.Current != null);
        }
    }

    private static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Check(bool condition) { if (!condition) throw new Exception("Host assertion failed"); }
}
