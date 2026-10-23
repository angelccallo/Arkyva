# Arkyva

Biblioteca open source y embebida para almacenar archivos mediante identificadores
GUID, con metadatos técnicos en SQL Server y contenido en FileSystem local o UNC/SMB.
El consumidor configura el repositorio una vez y utiliza una API asíncrona sin
administrar rutas internas ni tablas. Licencia [MIT](LICENSE).

Estado: **1.0.0-preview.1 preparada para Trusted Publishing; no publicada en NuGet.org**.
Consultar [el informe de preparación](docs/RELEASE_READINESS.md) para resultados,
limitaciones y condiciones antes de publicar.

## Compatibilidad y requisitos

- Cuatro bibliotecas netstandard2.0 y consumidor/pruebas net472.
- SQL Server con esquema arkyva instalado mediante scripts V001–V004, manualmente.
- Ruta local absoluta o UNC en Windows y permisos de lectura, escritura, creación,
  renombrado y limpieza de temporales propios; SMB requiere permisos de share y NTFS.
- SDK .NET y Developer Pack de .NET Framework 4.7.2 para compilar los ejemplos.

La ejecución se verifica en Windows con el runtime .NET Framework instalado; no
se afirma compatibilidad de toda la pila con cada runtime moderno de .NET ni con
cada configuración de IIS. Build no conecta SQL ni aplica migraciones.

## Distribución pública mediante NuGet.org

La publicación oficial se ejecutará manualmente desde GitHub Actions con OIDC,
según [NUGET_PUBLISHING.md](docs/NUGET_PUBLISHING.md). Todavía no se ejecutó.
**Después de confirmar que la versión existe en NuGet.org**, instalar en una
aplicación compatible:

```powershell
Install-Package Arkyva -Version 1.0.0-preview.1 -Source https://api.nuget.org/v3/index.json
```

En Visual Studio habilitar **Include prerelease**. El paquete principal resuelve
Arkyva.Core, Arkyva.Storage.FileSystem y Arkyva.Metadata.SqlServer automáticamente.
No se necesitan referencias a proyectos fuente ni construir proveedores concretos.

El feed local se usa solo para pruebas previas a la publicación; su procedimiento
está en [PACKAGING.md](docs/PACKAGING.md). No es la distribución pública oficial.

## Inicio rápido

Dentro de un método asíncrono, con configuración obtenida de la aplicación:

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
using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write))
    await file.Content.CopyToAsync(output);

var info = await arkyva.GetInfoAsync(id);
var page = await arkyva.ListAsync(new Arkyva.Core.Models.FileQuery { PageNumber = 1, PageSize = 20 });
bool available = await arkyva.ExistsAsync(id);
await arkyva.DeleteAsync(id); // Eliminación lógica; conserva contenido físico.
```

El llamador posee la entrada y debe liberar FileContent de las descargas. La instancia
construida puede reutilizarse; no mantiene conexiones ni streams abiertos globalmente.

## Características del MVP

- Upload, Download, GetInfo, List con filtros/paginación, Exists y Delete lógico.
- GUID y claves internas independientes del nombre original; validación contra traversal.
- Streaming, temporal exclusivo y publicación sin sobrescribir un definitivo existente.
- Metadatos SQL parametrizados: nombre, extensión, MIME, tamaño, referencias internas,
  fechas UTC y estados Pending, Active, Failed, Deleted.
- Concurrencia optimista mediante Revision y recibo SHA-256 tras publicación exitosa.
- Recuperación manual verificable y DryRun predeterminado; diagnóstico sin borrar
  definitivos ni temporales cuyo abandono no se pueda demostrar.
- Builder inmutable, composición manual opcional y ejemplo .NET Framework 4.7.2.

Los DTO públicos no contienen StorageKey, rutas ni conexiones. SQL no almacena binarios
ni se relaciona con tablas de negocio. El hash se utiliza para recuperación de recibos;
no es una auditoría automática de cada descarga.

## Seguridad y límites

La aplicación consumidora es responsable de **autenticar y autorizar cada acceso**;
conocer un FileId no concede autorización. También debe imponer límites de tamaño y
tipos permitidos: la política configurable de esos límites prevista en la especificación
no está implementada en esta preview. El MIME predeterminado es application/octet-stream.

El árbol físico debe estar protegido contra cambios externos de enlaces/directorios.
SQL y FileSystem no son una transacción atómica. Un fallo de red puede dejar un resultado
incierto; conservar el FileId y usar diagnóstico/recuperación, sin borrar a ciegas.
Los históricos sin recibo no se activan automáticamente. Los temporales inciertos se
reportan y conservan. SMB real, IIS y otras identidades/autenticaciones requieren
validación en el despliegue. No hay dashboard, OCR, versionamiento ni workers.

Véase [seguridad](docs/SECURITY.md) y [recuperación](docs/RECOVERY.md).

## Validación reproducible

```powershell
./scripts/Test.ps1                 # Ejecutables net472; SQL explícitamente omitido.
./scripts/Audit.ps1                # Avisos NuGet directos y transitivos.
./scripts/Test-Packages.ps1        # Inspección y consumidor nuevo desde feed local.
# Con ARKYVA_SQL_TEST_CONNECTION de pruebas, para incluir SQL aislado:
./scripts/Test.ps1 -RunSqlIntegration
```

No se usa dotnet test para estos ejecutables. Las pruebas SQL crean una base con nombre
único y una carpeta temporal, y eliminan solo sus recursos de prueba. No usar una
conexión de producción. El CI configura Windows para compilar, auditar, probar
y empaquetar sin publicar; el workflow de publicación separado requiere una acción
manual y aprobación del environment. SQL externo requiere ejecución manual y configuración protegida.

## Documentación

- [Inicio rápido, Web.config, IIS y ejemplo](docs/GETTING_STARTED.md)
- [Arquitectura y extensibilidad](docs/ARCHITECTURE.md)
- [SQL, esquema y migraciones](docs/SQLSERVER.md)
- [Proveedor FileSystem y SMB](docs/FILESYSTEM.md)
- [Servicio y contratos públicos](docs/SERVICE.md)
- [Consistencia y recuperación](docs/RECOVERY.md)
- [Paquetes y consumidor limpio](docs/PACKAGING.md)
- [Informe para publicación preview](docs/RELEASE_READINESS.md)
- [Publicación oficial OIDC, instalación y actualizaciones](docs/NUGET_PUBLISHING.md)

Código en [GitHub](https://github.com/angelccallo/Arkyva). Copyright 2026 angelccallo.
