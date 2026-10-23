# Arkyva
Lightweight, extensible file storage library for .NET.

## Estado

Fase 4: servicio principal implementado con los proveedores FileSystem y SQL Server.
Recuperación automática y configuración fluida pendientes.
Las bibliotecas utilizan .NET Standard 2.0, compatible con .NET Framework 4.7.2.

```powershell
dotnet build Arkyva.sln --configuration Release
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
