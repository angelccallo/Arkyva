# Arkyva
Lightweight, extensible file storage library for .NET.

## Estado

Fase 6: configuración fluida, servicio principal, proveedores FileSystem/SQL Server,
recuperación manual y ejemplo .NET Framework 4.7.2.
Las bibliotecas utilizan .NET Standard 2.0, compatible con .NET Framework 4.7.2.

Referencia `src/Arkyva/Arkyva.csproj` desde la aplicación; el builder compone los proveedores:

```csharp
using System;
using System.IO;
using Arkyva;

var arkyva = ArkyvaBuilder.Create()
    .UseSqlServer(connectionString)
    .UseFileSystem(storagePath)
    .Build();

Guid id;
using (var input = File.OpenRead(inputPath))
    id = await arkyva.UploadAsync(input, "documento.pdf");
using (var file = await arkyva.DownloadAsync(id))
using (var output = File.Create(outputPath))
    await file.Content.CopyToAsync(output);
```

Preparar SQL V001-V004 previamente. Build puede crear la carpeta; no conecta a SQL
ni instala el esquema. Ver [inicio rápido](docs/GETTING_STARTED.md) para configuración,
Web.config, ejemplo net472, ciclo de vida y permisos. NuGet todavía no está publicado.

```powershell
dotnet build Arkyva.sln --configuration Release
& ./tests/Arkyva.ConfigurationTests/bin/Release/net472/Arkyva.ConfigurationTests.exe
& ./tests/Arkyva.RecoveryTests/bin/Release/net472/Arkyva.RecoveryTests.exe
& ./tests/Arkyva.ServiceTests/bin/Release/net472/Arkyva.ServiceTests.exe
& ./tests/Arkyva.ContractTests/bin/Release/net472/Arkyva.ContractTests.exe
& ./tests/Arkyva.FileSystemTests/bin/Release/net472/Arkyva.FileSystemTests.exe
& ./tests/Arkyva.SqlServerTests/bin/Release/net472/Arkyva.SqlServerTests.exe
```

Las comprobaciones usan .NET Framework 4.7.2 en Windows, sin paquetes de pruebas.
Requieren su Developer Pack y un runtime compatible.
Consulta [la arquitectura](docs/ARCHITECTURE.md) y [los requisitos](docs/SPECIFICATION.md).

Consulta [el proveedor FileSystem](docs/FILESYSTEM.md) para rutas locales y UNC, uso y limitaciones.

Consulta [el proveedor SQL Server](docs/SQLSERVER.md) para instalar el esquema, usar el repositorio y configurar pruebas aisladas. Sin conexión de pruebas se ejecutan solo las validaciones offline y la integración SQL se marca como omitida.

Consulta [ArkyvaService](docs/SERVICE.md) para composición manual, carga, descarga, estados y manejo de errores.

Consulta [recuperación y concurrencia](docs/RECOVERY.md). La Fase 5 requiere aplicar V004 manualmente y actualizar todas las instancias; los temporales inciertos se diagnostican y conservan.
