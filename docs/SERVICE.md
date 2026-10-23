> Esta página describe la Fase 4. Desde la Fase 5, el flujo vigente calcula SHA-256,
> persiste el recibo antes de activar y usa revisiones optimistas. Consultar
> [RECOVERY.md](RECOVERY.md) para los cambios y el mantenimiento manual.

# Servicio principal — Fase 4

ArkyvaService reside en Arkyva.Core y depende solo de IStorageProvider e
IMetadataRepository. Core no referencia los proyectos concretos. La aplicación
compone las instancias manualmente; esta fase no implementa Arkyva.Configure,
builder, limpieza, reintentos ni reconciliación automática.

## Uso mediante inyección manual

```csharp
using System.IO;
using Arkyva.Core.Interfaces;
using Arkyva.Core.Models;
using Arkyva.Core.Services;
using Arkyva.Metadata.SqlServer.Repositories;
using Arkyva.Storage.FileSystem;

IStorageProvider storage = new FileSystemStorageProvider(baseDirectory);
IMetadataRepository metadata = new SqlServerMetadataRepository(connectionString);
IArkyvaService arkyva = new ArkyvaService(storage, metadata, "FileSystem");

Guid id;
using (Stream input = File.OpenRead(inputPath))
    id = await arkyva.UploadAsync(input, "documento.pdf");

using (FileContent document = await arkyva.DownloadAsync(id))
using (Stream output = File.Create(outputPath))
{
    string name = document.Info.OriginalName;
    string mime = document.Info.MimeType;
    long size = document.Info.SizeBytes;
    await document.Content.CopyToAsync(output);
}

var info = await arkyva.GetInfoAsync(id);
var page = await arkyva.ListAsync(new FileQuery { PageNumber = 1, PageSize = 20 });
bool availableNow = await arkyva.ExistsAsync(id);
await arkyva.DeleteAsync(id);
```

El esquema SQL debe instalarse previamente según [SQLSERVER.md](SQLSERVER.md).
La cuenta de ejecución y la ruta se configuran según [FILESYSTEM.md](FILESYSTEM.md).
El identificador de proveedor se registra en metadatos y debe permanecer estable
entre reinicios. Cada instancia de servicio utiliza un solo proveedor físico.
Si un registro identifica otro proveedor, Download devuelve no disponible y
Exists devuelve false sin acceder al proveedor equivocado. GetInfo, List y Delete
solo consultan o modifican metadatos y no requieren resolver el proveedor físico.

## Contratos públicos y cambios justificados

Se conservan UploadAsync(Stream, string), GetInfoAsync, ListAsync, ExistsAsync y
DeleteAsync. DownloadAsync cambia de Task<Stream> a Task<FileContent> porque ahora
la descarga necesita nombre, MIME y tamaño junto al contenido. Es un cambio de
contrato intencional: el consumidor debe utilizar el resultado en using y acceder
al stream mediante Content. No se mantiene otra firma duplicada de descarga.
FileContent.Info reutiliza el DTO FileInfo existente; no replica sus propiedades.

GetInfo devuelve un snapshot público para Active o null para ausentes y otros
estados, conservando el contrato inicial. No abre contenido ni garantiza que exista.
List devuelve únicamente DTOs públicos y consulta la página solicitada a metadatos;
no abre ni verifica archivos físicos. Los objetos son copias de tipo FileInfo, no
FileMetadata serializados con propiedades técnicas. FileContent también copia su
Info para impedir retener accidentalmente un modelo técnico.

List conserva Active por defecto y, para admitir todos los filtros de la Fase 3,
acepta estados explícitos Pending, Failed o Deleted; Status null solicita todos.
Esto amplía la decisión inicial de listar solo activos: consultar metadatos de un
registro eliminado no concede acceso a su contenido. Download siempre requiere
Active. Los filtros de ID, nombre exacto, extensión, estado e intervalo UTC se
transfieren al repositorio sin modificar la consulta del llamador. El orden y el
total corresponden al proveedor (CreatedUtc, FileId). Se validan páginas positivas,
tamaño entre 1 y 1000, enum, longitudes e intervalo UTC antes de consultar.

## Flujo de carga

1. Validar un stream legible y un nombre descriptivo sin directorios, caracteres
   de control, caracteres reservados de Windows, espacios de borde o punto final.
   Máximo 255 unidades UTF-16 de nombre y 30 de extensión. Se permite un nombre
   sin extensión, incluyendo dotfiles. No se utiliza el nombre como ruta física.
2. Generar FileId GUID y una clave yyyy/MM/<guid>.bin una sola vez. No hay
   reintentos internos ni cambios de clave durante una llamada.
3. Insertar metadatos Pending, fecha UTC, tamaño provisional cero y MIME
   application/octet-stream. La firma actual no recibe MIME y esta fase no hace
   detección de contenido ni añade un catálogo de tipos por extensión.
4. Transferir desde la posición actual del stream mediante una vista de lectura
   que cuenta bytes efectivamente consumidos. No utiliza Length, Seek ni carga
   completa en memoria. El llamador conserva la propiedad del stream, incluso
   cuando se produce un error.
5. Solo si SaveAsync termina correctamente, actualizar a Active y fijar SizeBytes
   con el contador. Una fuente vacía produce un archivo Active de cero bytes.

La cuenta presupone que un IStorageProvider válido guarda íntegramente y sin
transformaciones los bytes leídos antes de completar SaveAsync, como hace
FileSystem. No se realiza una segunda descarga ni se calcula checksum en esta fase.
El proveedor mantiene sus buffers acotados y sus reglas de publicación temporal.

### Ajuste necesario en SQL

En la Fase 3 SizeBytes era inmutable en todas las actualizaciones. Esto impedía
registrar Pending antes de leer un stream no posicionable y luego fijar su tamaño
real. UpdateAsync ahora permite cambiar SizeBytes únicamente cuando el estado
persistido es Pending y el nuevo estado es Active, en la misma sentencia SQL que
activa el registro. Fuera de esa transición sigue siendo inmutable; tampoco se
cambia proveedor, clave ni fecha de creación. No hace falta cambiar el esquema SQL
ni el contrato IMetadataRepository. Se añadió la comprobación de integración SQL
para finalizar tamaño y rechazar cambios posteriores; todavía requiere servidor.

## Errores de carga y resultados inciertos

| Fallo | Acción del servicio |
| --- | --- |
| Insertar Pending | No llama a almacenamiento; propaga error de metadatos con FileId |
| Save o lectura de entrada | Intenta marcar Failed una vez; nunca borra contenido |
| Marcar Failed | Conserva causa de escritura y causa de actualización juntas; puede quedar Pending |
| Activar | Propaga error de metadatos; no degrada estado ni borra contenido |

El servidor SQL o SMB puede confirmar una operación después de que el cliente
observe un error. Por eso un error de inserción puede haber dejado Pending y un
error de activación puede haber dejado Active con contenido completo. Save puede
haber publicado antes del error: se conserva el contenido incluso si se pudo
marcar Failed. No hay transacción atómica entre sistemas, rollback físico ciego
ni promesa de ejecución exactamente una vez.

Los errores de carga contienen el FileId público para diagnóstico posterior.
Repetir UploadAsync es una operación nueva con otro ID; no es una API idempotente
entre llamadas. La recuperación de registros y contenido retenido queda pendiente
para la Fase 5, sin procesos implementados en esta fase.

## Descarga, disponibilidad y streams

Download consulta metadatos, exige Active y coincidencia de proveedor, abre el
contenido y devuelve FileContent con sus metadatos públicos. El stream de descarga
es una envoltura de lectura que oculta tipos concretos como FileStream y su Name.
Permite CopyToAsync y conserva capacidades de posicionamiento que admita el stream
subyacente. Los errores de E/S durante la lectura conservan su causa mediante una
excepción de almacenamiento con mensaje principal seguro. Dispose del resultado
libera el stream subyacente; no libera los proveedores ni el repositorio.

Exists consulta estado y proveedor y después la existencia física. No convierte
fallos de acceso o persistencia en false. Su resultado es una observación puntual:
una eliminación o pérdida de acceso puede ocurrir inmediatamente después.

Sin bloquear ambos sistemas, una descarga que ya observó Active puede coincidir
con una eliminación lógica y continuar. El borrado lógico no revoca streams ya
abiertos. No hay garantía de instantánea entre metadatos y contenido ni protección
frente a modificaciones externas del repositorio físico.

## Eliminación lógica

Delete acepta registros Pending, Active y Failed; los cambia a Deleted con fecha
UTC no anterior a CreatedUtc. No llama a IStorageProvider.DeleteAsync. Repetir la
eliminación o solicitarla para un ID ausente es idempotente y conserva la fecha
persistida. Después, una nueva descarga que observe Deleted será rechazada y el
listado predeterminado lo excluirá. Los metadatos siguen accesibles al repositorio.

Si Update falla, se hace una sola consulta de confirmación: si ya está Deleted,
la operación se considera satisfecha (borrado concurrente o confirmación tardía).
En otro caso se propaga la causa inicial; si también falla la consulta, se conservan
ambas causas. No se reintenta ninguna escritura ni se inicia un ciclo de recuperación.

## Excepciones

- ArgumentException y subtipos: entrada, identidad de proveedor, ID o filtros inválidos.
- ArkyvaFileNotFoundException (deriva de FileNotFoundException): registro o contenido
  físico inexistente; incluye FileId, sin FileName físico.
- ArkyvaFileUnavailableException: estado no Active o proveedor diferente; incluye
  FileId y el estado observado, sin claves ni identificadores del proveedor.
- ArkyvaStorageException: fallo de escritura, apertura, verificación o consumo.
- ArkyvaMetadataException: fallo al registrar, consultar, listar, activar o eliminar.

Las dos últimas incluyen FileId cuando corresponde y conservan InnerException.
Un fallo secundario se conserva mediante AggregateException. Los mensajes del
servicio no incorporan rutas, claves, cadenas de conexión ni el texto del proveedor.
InnerException y ToString son diagnóstico técnico y pueden contener datos sensibles:
no deben mostrarse directamente a usuarios finales. Los proveedores mantienen sus
excepciones originales para uso directo, fuera de ArkyvaService.

## Validación

```powershell
dotnet build Arkyva.sln --configuration Release
& ./tests/Arkyva.ServiceTests/bin/Release/net472/Arkyva.ServiceTests.exe
& ./tests/Arkyva.ContractTests/bin/Release/net472/Arkyva.ContractTests.exe
& ./tests/Arkyva.FileSystemTests/bin/Release/net472/Arkyva.FileSystemTests.exe
& ./tests/Arkyva.SqlServerTests/bin/Release/net472/Arkyva.SqlServerTests.exe
```

Las pruebas del servicio usan fakes con snapshots y reglas de estados/tamaño
compatibles con el contrato SQL, sin afirmar que sustituyen una prueba del servidor.
Cubren éxito, IDs y claves, orden Pending/Save/Active, streams grandes no posicionables,
entradas inválidas, cada fase fallida, confirmaciones inciertas, causas secundarias,
metadatos públicos, filtros, paginación, estados, disposición, errores durante lectura,
disponibilidad, eliminación lógica idempotente y concurrente y ausencia de borrado
físico. No requieren SQL Server ni SMB. No hay nuevas dependencias de paquetes.

La integración SQL real y el flujo conjunto con SQL Server siguen pendientes de un
servidor de pruebas configurado. El runner SQL muestra SKIP cuando no existe
ARKYVA_SQL_TEST_CONNECTION; no se informa esa parte como prueba satisfactoria.
No se implementaron recuperación, reconciliación, limpieza física ni builder.

Resultado ejecutado de la Fase 4: solución Release con siete proyectos, cero errores
y cero advertencias; 41 escenarios del servicio, 10 comprobaciones de contratos,
21 FileSystem y 58 validaciones SQL offline satisfactorias. La integración SQL
fue omitida explícitamente por falta de conexión de pruebas configurada.
