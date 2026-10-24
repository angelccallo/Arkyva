# Arkyva â€” preview

Lightweight embedded file storage for .NET Standard 2.0, consumable from .NET
Framework 4.7.2. SQL Server stores technical metadata; FileSystem stores streamed
content on a local or Windows UNC path. MIT licensed, by Angel Ccallo.

After the authorized public release is confirmed on NuGet.org, install only the integration package. This version has NOT been published during preparation:

```powershell
Install-Package Arkyva -Version 1.0.0-preview.2 -Source https://api.nuget.org/v3/index.json
```

```csharp
using Arkyva;

ArkyvaHost.Configure(options =>
{
    options.UseSqlServer(connectionString);
    options.UseFileSystem(storagePath);
});

Guid id = await ArkyvaHost.Current.UploadAsync(stream, "document.pdf");
using (var file = await ArkyvaHost.Current.DownloadAsync(id))
    await file.Content.CopyToAsync(destination);
```

ArkyvaHost is optional and available from 1.0.0-preview.2: configure synchronously
once per AppDomain at startup; Current fails before successful configuration and
duplicate configuration fails. Failed attempts can be retried. IIS recycling creates
a new domain; no shutdown disposal is required. Use the unchanged ArkyvaBuilder
for dependency injection, isolated tests or multiple independent instances.

Install SQL scripts V001â€“V004 manually before using the service. Scripts ship in
Arkyva.Metadata.SqlServer under contentFiles/any/any/Scripts. No automatic migrations,
background workers, authentication, authorization or physical deletion on logical
Delete. The consuming application must authenticate and authorize all file access.

RecoverAsync is manual, dry-run by default, and activates only verifiable receipts.
Uncertain content and old temporaries are retained. SQL/SMB cannot form one atomic
transaction. A successful Build does not validate SQL connectivity or schema.

This preview is not a stable release. Consult the readiness report for actual
validation and outstanding SQL/IIS/SMB checks before production use.

[Source and documentation](https://github.com/angelccallo/Arkyva)
| [Getting started](https://github.com/angelccallo/Arkyva/blob/main/docs/GETTING_STARTED.md)
| [Readiness report](https://github.com/angelccallo/Arkyva/blob/main/docs/RELEASE_READINESS.md)

[Publishing process](https://github.com/angelccallo/Arkyva/blob/main/docs/NUGET_PUBLISHING.md) uses GitHub Actions and NuGet Trusted Publishing; a local feed is only a pre-release test.
