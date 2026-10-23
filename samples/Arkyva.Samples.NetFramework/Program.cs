using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using Arkyva;
using Arkyva.Core.Exceptions;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { return RunAsync(args).GetAwaiter().GetResult(); }
        catch (ArkyvaMetadataException error) { Console.Error.WriteLine("Metadata operation failed. FileId: " + error.FileId + ". Check SQL connectivity, permissions and schema V001-V004."); return 1; }
        catch (ArkyvaStorageException error) { Console.Error.WriteLine("Storage operation failed. FileId: " + error.FileId + ". Check the storage permissions and availability."); return 1; }
        catch (ArkyvaFileNotFoundException error) { Console.Error.WriteLine("File not found. FileId: " + error.FileId); return 1; }
        catch (ArkyvaFileUnavailableException error) { Console.Error.WriteLine("File unavailable. FileId: " + error.FileId); return 1; }
        catch (ConfigurationErrorsException) { Console.Error.WriteLine("Invalid application configuration."); return 2; }
        catch (ArgumentException) { Console.Error.WriteLine("Invalid configuration or arguments. Supply SQL settings, an absolute storage directory and valid file names."); return 2; }
        catch (InvalidOperationException) { Console.Error.WriteLine("Configuration incomplete. Supply ARKYVA_CONNECTION_STRING and ARKYVA_STORAGE_PATH or their App.config entries."); return 2; }
        catch (IOException) { Console.Error.WriteLine("File operation failed. Verify input, a new output path and directory availability."); return 1; }
        catch (UnauthorizedAccessException) { Console.Error.WriteLine("Access denied. Verify permissions for the application's identity."); return 1; }
        catch (Exception) { Console.Error.WriteLine("The sample failed. Use trusted diagnostics; do not expose exception details or credentials to end users."); return 1; }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help") { Usage(); return 0; }
        bool checkOnly = args.Length == 1 && args[0] == "--check-config";
        if (!checkOnly && args.Length != 2) { Usage(); return 2; }
        string connectionString = Environment.GetEnvironmentVariable("ARKYVA_CONNECTION_STRING") ??
            ConfigurationManager.ConnectionStrings["Arkyva"]?.ConnectionString;
        string storagePath = Environment.GetEnvironmentVariable("ARKYVA_STORAGE_PATH") ?? ConfigurationManager.AppSettings["Arkyva:StoragePath"];
        IArkyvaService arkyva = ArkyvaBuilder.Create().UseSqlServer(connectionString).UseFileSystem(storagePath).Build();
        if (checkOnly)
        {
            Console.WriteLine("Configuration built on .NET Framework. SQL connectivity and schema were NOT checked.");
            return 0;
        }
        Guid id;
        using (Stream input = File.OpenRead(args[0]))
            id = await arkyva.UploadAsync(input, Path.GetFileName(args[0]));
        Console.WriteLine("Uploaded FileId: " + id);
        using (FileContent file = await arkyva.DownloadAsync(id))
        using (Stream output = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await file.Content.CopyToAsync(output);
        var info = await arkyva.GetInfoAsync(id);
        Console.WriteLine("Metadata: " + info.OriginalName + ", " + info.SizeBytes + " bytes, " + info.MimeType);
        var page = await arkyva.ListAsync(new FileQuery { PageNumber = 1, PageSize = 20, FileId = id });
        Console.WriteLine("Page results: " + page.Items.Count + "; total: " + page.TotalCount);
        Console.WriteLine("Available: " + await arkyva.ExistsAsync(id));
        await arkyva.DeleteAsync(id);
        Console.WriteLine("Logically deleted. Available: " + await arkyva.ExistsAsync(id));
        Console.WriteLine("The stored physical content is retained; no cleanup is performed.");
        return 0;
    }

    private static void Usage()
    {
        Console.WriteLine("Arkyva.Samples.NetFramework <input-file> <new-output-file>");
        Console.WriteLine("--check-config builds without connecting to SQL; --help shows this message.");
        Console.WriteLine("Configure App.config or ARKYVA_CONNECTION_STRING and ARKYVA_STORAGE_PATH. Install SQL schema V001-V004 first.");
    }
}
