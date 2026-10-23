# Proveedor de metadatos SQL Server — Fase 3

El repositorio implementa `IMetadataRepository` sin acceder a archivos ni coordinar
FileSystem. Usa ADO.NET con una referencia directa a Microsoft.Data.SqlClient 6.1.7
(rama LTS), cuyos assets admiten netstandard2.0 y .NET Framework 4.6.2 o superior:
[paquete oficial](https://www.nuget.org/packages/Microsoft.Data.SqlClient/6.1.7),
[soporte y distribución](https://learn.microsoft.com/en-us/sql/connect/ado-net/download-microsoft-sqlclient-data-provider).
No usa Entity Framework, un contenedor DI, procesos adicionales ni conexiones permanentes.

## Preparación de la base

Utilizar SQL Server 2012 o superior con nivel de compatibilidad 110 o superior
(paginación OFFSET/FETCH y SHA2_256). Esta es la versión mínima exigida por el SQL
utilizado, no una afirmación de soporte del fabricante de versiones antiguas.

Crear o seleccionar una base antes de ejecutar, en orden, los archivos de
`src/Arkyva.Metadata.SqlServer/Scripts` con SSMS o sqlcmd:

1. `V001_CreateSchema.sql`: crea únicamente el esquema `arkyva` si falta.
2. `V002_CreateFilesTable.sql`: crea `arkyva.Files` y sus restricciones si falta.
3. `V003_CreateIndexes.sql`: crea los índices faltantes por nombre.
4. `V004_AddRecoveryEvidence.sql`: añade revisión y recibo SHA-256 (Fase 5).

Ejemplo con autenticación integrada (sustituir servidor y base):

```powershell
sqlcmd -S SERVIDOR -d BASE -E -b -i src/Arkyva.Metadata.SqlServer/Scripts/V001_CreateSchema.sql
sqlcmd -S SERVIDOR -d BASE -E -b -i src/Arkyva.Metadata.SqlServer/Scripts/V002_CreateFilesTable.sql
sqlcmd -S SERVIDOR -d BASE -E -b -i src/Arkyva.Metadata.SqlServer/Scripts/V003_CreateIndexes.sql
sqlcmd -S SERVIDOR -d BASE -E -b -i src/Arkyva.Metadata.SqlServer/Scripts/V004_AddRecoveryEvidence.sql
```

Los scripts contienen separadores GO y opciones SET necesarias para el índice
calculado. Se distribuyen junto al proyecto/paquete. Son aditivos y repetibles para
este esquema; no corrigen una tabla preexistente incompatible ni migran sus datos.
No hay tabla adicional de versiones: el operador registra los V00x aplicados en su
proceso de despliegue. Ningún constructor ni operación aplica migraciones.

## Tabla e índices

Los nombres conservan los modelos de Core, en lugar de crear un segundo modelo:

| Columna | Tipo | Regla |
| --- | --- | --- |
| FileId | uniqueidentifier | PK, no vacío |
| OriginalName | nvarchar(255) | Obligatorio |
| Extension | nvarchar(30) | Opcional |
| MimeType | nvarchar(255) | Opcional |
| SizeBytes | bigint | No negativo |
| ProviderId | nvarchar(100) | Obligatorio |
| StorageKey | nvarchar(1000) | Obligatorio |
| Status | varchar(20) | Pending, Active, Deleted o Failed |
| CreatedUtc | datetime2(7) | Obligatorio, default SYSUTCDATETIME |
| UpdatedUtc | datetime2(7) | Técnico, actualizado por Update |
| DeletedUtc | datetime2(7) | Solo para Deleted, no anterior a creación |
| StorageIdentityHash | binary(32) calculado persistido | SHA-256 de proveedor y clave |
| Revision | bigint | Revisión optimista, default 0 |
| CompletionSha256 | char(64) | Recibo de publicación; null para históricos |

Se usa precisión de siete decimales para conservar los ticks de DateTime y evitar
conflictos al comprobar campos inmutables. El repositorio exige DateTimeKind.Utc y
restituye ese Kind al leer; SQL Server no almacena zonas horarias.
UpdatedUtc no se añade al modelo público porque solo es información de auditoría
mínima de la tabla, no un token de concurrencia.

Además de PK_arkyva_Files:

- UX_arkyva_Files_StorageIdentity: único sobre la huella SHA-256. Reserva la pareja
  proveedor/clave incluso si el registro está eliminado.
- IX_arkyva_Files_Status_CreatedUtc_FileId: estado y orden paginado; incluye nombre
  y extensión.
- IX_arkyva_Files_CreatedUtc_FileId: orden para consultas sobre todos los estados.

NVARCHAR(1000) más el proveedor exceden el límite de bytes de una clave de índice
convencional ([límites oficiales](https://learn.microsoft.com/en-us/sql/sql-server/maximum-capacity-specifications-for-sql-server)).
La huella utiliza bytes UTF-16 con longitud del proveedor como prefijo para evitar
ambigüedad en la concatenación. Una colisión criptográfica excepcional rechazaría
la segunda inserción como duplicada; nunca reemplaza ni mezcla registros.
La igualdad de proveedor/clave es binaria y distingue mayúsculas. El futuro servicio
debe generar claves canónicas acordes al proveedor físico; este repositorio no
conoce las reglas de equivalencia de rutas FileSystem.

## Inicialización y operaciones

```csharp
using System;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using Arkyva.Metadata.SqlServer.Repositories;

// Obtener la cadena desde la configuración segura de la aplicación.
IMetadataRepository repository = new SqlServerMetadataRepository(connectionString);
var metadata = new FileMetadata
{
    FileId = Guid.NewGuid(), OriginalName = "documento.pdf", Extension = ".pdf",
    MimeType = "application/pdf", SizeBytes = 123,
    ProviderId = "FileSystem", StorageKey = "2026/10/identificador-unico.bin",
    Status = FileStatus.Pending, CreatedUtc = DateTime.UtcNow
};
await repository.AddAsync(metadata);
FileMetadata stored = await repository.GetAsync(metadata.FileId);

// Tras un SaveAsync exitoso y cálculo completo de tamaño/hash, usar
// IRecoveryMetadataRepository.TryRecordUploadAsync antes de activar.
// Para consumir archivos normalmente, utilizar ArkyvaService (ver SERVICE.md).
bool active = await repository.ExistsAsync(stored.FileId);

var page = await repository.ListAsync(new FileQuery
{
    PageNumber = 1, PageSize = 50, OriginalName = "documento.pdf",
    Status = FileStatus.Active,
    CreatedFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    CreatedBeforeUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
});

stored.Status = FileStatus.Deleted;
stored.DeletedUtc = DateTime.UtcNow;
await repository.UpdateAsync(stored); // Solo metadatos; no elimina contenido físico.
```

La cadena exige Data Source e Initial Catalog. Cada operación abre y libera su
propia conexión; SqlClient administra el pooling según la cadena configurada.
No se altera la política TLS del consumidor: configurar certificados de confianza;
TrustServerCertificate solo corresponde a entornos de desarrollo controlados.
No hay cadenas fijas ni credenciales en el código del proveedor.

- AddAsync registra cualquier estado válido (Pending es el valor inicial del enum),
  sin upsert ni sustitución. El llamador debe proporcionar un identificador y fecha UTC.
- GetAsync obtiene cualquier estado o null para un identificador ausente.
- ExistsAsync conserva el contrato inicial: true solo para Active. Para comprobar
  existencia en cualquier estado, usar GetAsync.
- ListAsync devuelve página y TotalCount filtrado. No carga el repositorio completo.
- UpdateAsync actualiza nombre, extensión, MIME y estado/fecha de eliminación.
  Identificador, proveedor, clave y fecha de creación deben coincidir con
  los valores persistidos. Desde la Fase 4, el tamaño puede fijarse durante
  Pending → Active para finalizar cargas no posicionables; en otros estados
  debe coincidir con el valor persistido. No es un mecanismo para sustituir contenido físico.
  Un registro inexistente no se inserta.

## Estados y concurrencia

| Estado actual | Nuevos estados permitidos |
| --- | --- |
| Pending | Active, Failed, Deleted |
| Active | Failed, Deleted |
| Failed | Deleted |
| Deleted | Ningún otro estado |

Se permite mantener el mismo estado; en Deleted debe conservarse la misma fecha
de eliminación. Los cambios se realizan en una única sentencia UPDATE condicionada
por el estado actual persistido y por los campos inmutables. No hay lectura previa
seguida de escritura sin protección. No se permiten transiciones hacia atrás ni
reactivación de Deleted; el reinicio de Failed requerirá una decisión explícita en
una futura fase. La tabla valida estados y coherencia de fechas; la política de
transiciones es responsabilidad de este repositorio, no de un trigger.

Desde la Fase 5, todas las actualizaciones comprueban Revision y la incrementan
al tener éxito; los snapshots atrasados ya no siguen la política de última escritura.
No se permite cambiar el recibo ni el tamaño mediante UpdateAsync; el recibo se
fija con TryRecordUploadAsync antes de activar. Failed → Active se reserva a
TryActivateRecoveredAsync tras verificación de contenido.
Una transición rechazada, campos inmutables distintos o registro ausente producen
InvalidOperationException sin intentar adivinar la causa mediante otra consulta
que podría sufrir carreras. No hay transacción con FileSystem ni reintentos
ocultos tras fallos de red o conflictos.

## Filtros y paginación

FileQuery conserva sus propiedades y añade filtros opcionales FileId, Status,
CreatedFromUtc (inclusivo) y CreatedBeforeUtc (exclusivo). Status es Active por
defecto para preservar el comportamiento de la Fase 1; null permite consultar
todos los estados en el repositorio de metadatos. Desde la Fase 4, el servicio usa Active por defecto y admite filtros explícitos
de otros estados para consultar metadatos; descargar contenido sigue exigiendo Active.

Nombre y extensión son exactos y distinguen mayúsculas y espacios finales. Se
utiliza colación BIN2 y DATALENGTH para evitar la equivalencia de espacios finales
de SQL Server. Todos los filtros se combinan con AND. Las fechas deben ser UTC y
formar un intervalo no vacío cuando ambos extremos están presentes.

PageNumber empieza en uno; PageSize admite 1 a 1000. El límite acota la memoria de
cada consulta. El desplazamiento se calcula en long para evitar desbordamientos.
El orden es CreatedUtc, FileId ascendente (orden GUID propio de SQL Server). Una
página fuera del total devuelve Items vacío y conserva TotalCount.

El recuento y la página comparten una transacción corta Serializable para coherencia
ante inserciones concurrentes. Esto puede bloquear escritores o producir deadlocks;
los SqlException se propagan. Las distintas solicitudes de página son transacciones
independientes y no representan una instantánea global persistente.

## Excepciones y permisos

Se validan antes de conectar los campos obligatorios, tamaños máximos, FileId no
vacío, tamaño no negativo, enum válido, fechas UTC y coherencia de DeletedUtc.
Los valores opcionales null se envían como DBNull, sin inferencia de tipos mediante
AddWithValue. Todas las entradas son SqlParameter con tipos/tamaños explícitos;
solo se concatenan fragmentos SQL constantes en el repositorio.

- ArgumentException y subtipos: configuración, campos o filtros inválidos.
- InvalidOperationException: actualización rechazada según las reglas anteriores.
- SqlException: duplicados (2601/2627), permisos, restricciones, esquema ausente,
  conexión, timeout o deadlock. Se conserva la excepción original de SqlClient.

Para instalar se necesitan permisos de crear esquema/tabla/índices en la base de
destino. La cuenta de ejecución solo necesita CONNECT y SELECT, INSERT, UPDATE
sobre arkyva.Files, no DELETE ni CREATE DATABASE. El proveedor configura las opciones
SET necesarias del índice calculado al abrir cada conexión.

## Pruebas reproducibles

```powershell
dotnet build Arkyva.sln --configuration Release
& ./tests/Arkyva.SqlServerTests/bin/Release/net472/Arkyva.SqlServerTests.exe
```

Sin ARKYVA_SQL_TEST_CONNECTION ejecuta únicamente validaciones offline y muestra
SKIP para la integración SQL. No hay un sustituto en memoria ni éxito SQL simulado.
Se mantiene el estilo de ejecutables net472 del repositorio; dotnet test no los
descubre. Código de salida 1 significa fallo; salida 0 con SKIP no significa que
SQL Server haya sido probado.

Para integración, configurar ARKYVA_SQL_TEST_CONNECTION mediante el entorno seguro
del proceso con una conexión a un servidor exclusivo para pruebas. La cuenta de
pruebas necesita CREATE DATABASE y capacidad de eliminar la base que crea.
El runner ignora Initial Catalog y usa master solamente para crear una base nueva
ArkyvaTests_<guid>. Aplica scripts dos veces en esa base y la elimina en finally;
nunca limpia ni modifica una base preexistente. No admite AttachDBFilename ni
User Instance. Una conexión configurada que falla produce fallo, no SKIP. Un cierre
abrupto puede dejar la base de pruebas, cuyo nombre se reconoce por ese prefijo.

Las pruebas SQL preparadas cubren roundtrip y precisión UTC, ausentes, existencia
activa, duplicados de ID y pareja física, claves máximas, campos opcionales,
estados y eliminación, protección de campos inmutables, paginación, filtros exactos
y de fechas/estado/ID, entradas de inyección como datos, restricciones SQL y
concurrencia de inserciones y estados. No crean archivos físicos.

En el entorno de esta fase no hay una instancia SQL lista: LocalDB está instalado
pero su instancia automática no está creada y no hay conexión de pruebas configurada.
Resultado real: compilación Release sin errores ni advertencias; 58 comprobaciones
offline SQL, 9 de contratos y 21 FileSystem satisfactorias. Las pruebas de integración
SQL no se ejecutaron; quedan pendientes de validar el proveedor contra un servidor real.

La integración mediante ArkyvaService se implementó en la Fase 4; ver [SERVICE.md](SERVICE.md).
La configuración fluida, reconciliación y recuperación automática siguen pendientes.

## Despliegue de Fase 5

Aplicar V004 con todos los escritores detenidos y actualizar todas las instancias:
no mezclar binarios anteriores que no comprueben/incrementen Revision. Los métodos
nuevos y las condiciones vigentes sustituyen las reglas históricas de finalización
de tamaño durante Pending → Active descritas para la Fase 4.
Consultar [RECOVERY.md](RECOVERY.md) para el protocolo de recibo, límites y pruebas.
