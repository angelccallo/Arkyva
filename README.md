# Arkyva

Biblioteca open source y embebida para almacenar archivos mediante identificadores
GUID, con metadatos técnicos en SQL Server y contenido en FileSystem local o UNC/SMB.
El consumidor configura el repositorio una vez y utiliza una API asíncrona sin
administrar rutas internas ni tablas. Licencia [MIT](LICENSE).

Estado del código: **1.0.0-preview.4 en preparación**, incorpora E/S síncrona nativa además de la API Async.
La nueva versión aún no se publica desde este repositorio de trabajo.
Véase [CHANGELOG.md](CHANGELOG.md) para los cambios de esta versión.
Consultar [el informe de preparación](docs/RELEASE_READINESS.md) para resultados,
limitaciones y condiciones antes de publicar.

## Compatibilidad y requisitos

- Cuatro bibliotecas con targets net472 y netstandard2.0; consumidor/pruebas net472.
- SQL Server con esquema arkyva instalado mediante scripts V001–V004, manualmente.
- Ruta local absoluta o UNC en Windows y permisos de lectura, escritura, creación,
  renombrado y limpieza de temporales propios; SMB requiere permisos de share y NTFS.
- SDK .NET y Developer Pack de .NET Framework 4.7.2 para compilar los ejemplos.

La ejecución se verifica en Windows con el runtime .NET Framework instalado; no
se afirma compatibilidad de toda la pila con cada runtime moderno de .NET ni con
cada configuración de IIS. Build no conecta SQL ni aplica migraciones.

## Distribución pública mediante NuGet.org

La publicación de preview.4 se ejecutará manualmente desde GitHub Actions con OIDC,
según [NUGET_PUBLISHING.md](docs/NUGET_PUBLISHING.md).
**Después de confirmar que la versión existe en NuGet.org**, instalar en una
aplicación compatible:

```powershell
Install-Package Arkyva -Version 1.0.0-preview.4 -Source https://api.nuget.org/v3/index.json
```

En Visual Studio habilitar **Include prerelease**. El paquete principal resuelve
Arkyva.Core, Arkyva.Storage.FileSystem y Arkyva.Metadata.SqlServer automáticamente.
No se necesitan referencias a proyectos fuente ni construir proveedores concretos.

NuGet selecciona lib/net472 para un consumidor .NET Framework 4.7.2. En ese target
el proveedor SQL usa System.Data.SqlClient de System.Data del Framework, sin instalar
el paquete NuGet homónimo ni Microsoft.Data.SqlClient. En la medición limpia se pasó
de 45 paquetes a 4 y de 11 binding redirects a 0; no implica que una aplicación WCF
existente pueda eliminar sus otros paquetes o redirects sin revisarlos.

netstandard2.0 conserva Microsoft.Data.SqlClient 6.1.7 y Pkcs 8.0.2. El proveedor de
Framework es heredado: mantener Windows/.NET parcheados y revisar sus limitaciones
antes de actualizar, especialmente conexiones Strict o autenticación Entra avanzada.
Arkyva conserva Encrypt=True si se omite; no desactiva validación de certificados.
Véase [medición, seguridad y diferencias SQL](docs/DEPENDENCIES.md).
No se añade integración específica con ASP.NET Core ni nuevos proveedores.

El feed local se usa solo para pruebas previas a la publicación; su procedimiento
está en [PACKAGING.md](docs/PACKAGING.md). No es la distribución pública oficial.

## Inicio rápido

Configurar una vez como antes. Después se puede elegir una modalidad por operación:

```csharp
using Arkyva;

// Endpoint síncrono tradicional (por ejemplo, WCF):
using (var stream = File.OpenRead("documento.pdf"))
{
    Guid id = ArkyvaHost.Current.Upload(stream, "documento.pdf");
}

// Método asíncrono:
using (var stream = File.OpenRead("documento.pdf"))
{
    Guid id = await ArkyvaHost.Current.UploadAsync(stream, "documento.pdf");
}
```

Ambas modalidades preservan claves, estados, SHA-256, publicación temporal y revisión
SQL. La síncrona utiliza E/S síncrona y ocupa el hilo mientras trabaja; no bloquea
Tasks para llamar Async. La asíncrona conserva E/S asíncrona y se recomienda cuando
la escalabilidad de servidores bajo concurrencia es prioritaria. No usar la síncrona
indiscriminadamente para evitar await en aplicaciones web modernas.

Compatibilidad: IArkyvaService conserva sus seis miembros Async para no romper
implementadores existentes. Las operaciones nativas se declaran en
ISynchronousArkyvaService; con `using Arkyva`, las extensiones permiten invocarlas
directamente sobre Current sin cambiar el host ni su tipo. Un servicio/proveedor
personalizado solo Async sigue funcionando; su uso síncrono no soportado falla
claramente sin simularlo con tareas. Véase [las dos modalidades y sus contratos](docs/GETTING_STARTED.md#api-síncrona-nativa-y-api-asíncrona).

Para una aplicación con una sola configuración, inicializar una vez al arrancar:

```csharp
using System;
using System.IO;
using Arkyva;

ArkyvaHost.Configure(options =>
{
    options.UseSqlServer(connectionString);
    options.UseFileSystem(storagePath);
});

// Desde cualquier componente, dentro de su método asíncrono:
var arkyva = ArkyvaHost.Current;
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

Configure construye mediante ArkyvaBuilder; Current devuelve siempre esa misma
instancia. Current antes de configurar y una segunda configuración exitosa lanzan
InvalidOperationException. Un intento fallido no publica servicio y permite reintentar;
Build puede haber creado carpetas. El callback debe ser síncrono, sin async void,
sin configurar recursivamente ni esperar otra llamada Configure. No conservar options:
solo es válido durante ese callback. No hay Reset ni reemplazo público.

Para inyección de dependencias, pruebas aisladas o múltiples instancias, la API
anterior continúa disponible sin cambios y no configura ArkyvaHost:

```csharp
var arkyva = ArkyvaBuilder.Create()
    .UseSqlServer(connectionString)
    .UseFileSystem(storagePath)
    .Build();
```

En ASP.NET/WCF alojado en IIS, llamar Configure una vez desde Application_Start
del arranque existente y usar ArkyvaHost.Current en los endpoints, sin HttpContext
ni acceso a Global. Cada AppDomain conserva su instancia; el reciclado permite
configurar nuevamente en el nuevo dominio y no ejecuta recuperación automática.
No requiere Dispose/Application_End; liberar siempre cada descarga. Véase el
[ejemplo WCF y las pruebas aisladas](docs/GETTING_STARTED.md#arkyvahost-en-aspnet-y-wcf).

## Características del MVP

- Upload, Download, GetInfo, List con filtros/paginación, Exists y Delete lógico.
- GUID y claves internas independientes del nombre original; validación contra traversal.
- Streaming, temporal exclusivo y publicación sin sobrescribir un definitivo existente.
- Metadatos SQL parametrizados: nombre, extensión, MIME, tamaño, referencias internas,
  fechas UTC y estados Pending, Active, Failed, Deleted.
- Concurrencia optimista mediante Revision y recibo SHA-256 tras publicación exitosa.
- Recuperación manual verificable y DryRun predeterminado; diagnóstico sin borrar
  definitivos ni temporales cuyo abandono no se pueda demostrar.
- Host opcional de configuración única, builder inmutable y ejemplo .NET Framework 4.7.2.

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
