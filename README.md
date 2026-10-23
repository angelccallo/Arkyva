# Arkyva
Lightweight, extensible file storage library for .NET.

## Estado

Fase 1: solucion y contratos; proveedores todavia sin implementacion.
Las bibliotecas utilizan .NET Standard 2.0, compatible con .NET Framework 4.7.2.

```powershell
dotnet build Arkyva.sln --configuration Release
& ./tests/Arkyva.ContractTests/bin/Release/net472/Arkyva.ContractTests.exe
```

Las comprobaciones usan .NET Framework 4.7.2 en Windows, sin paquetes de pruebas.
Requieren su Developer Pack y un runtime compatible.
Consulta [la arquitectura](docs/ARCHITECTURE.md) y [los requisitos](docs/SPECIFICATION.md).
