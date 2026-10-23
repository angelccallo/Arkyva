# Consistencia, recuperación y concurrencia — Fase 5

La recuperación es una llamada explícita. No se inicia desde constructores, no
programa tareas y no necesita workers, servicios ni paquetes nuevos. Se prioriza
conservar contenido frente a recuperar operaciones que no se puedan verificar.

## Preparación y compatibilidad

Aplicar manualmente V001 a V004 en orden. V004_AddRecoveryEvidence.sql es aditivo:

- Revision BIGINT NOT NULL DEFAULT 0: revisión optimista por registro.
- CompletionSha256 CHAR(64) NULL: recibo SHA-256 hexadecimal minúsculo de publicación.

No se eliminan ni reescriben registros existentes y el hash antiguo permanece null.
Los registros Active anteriores siguen funcionando; los Pending/Failed sin recibo
no pueden activarse automáticamente. El código nuevo requiere V004 incluso para
leer metadatos. Detener todos los escritores durante el despliegue y actualizar
todas las instancias: un binario antiguo o SQL manual que ignore Revision puede
invalidar las garantías de concurrencia. Ninguna operación aplica migraciones.

FileMetadata añade ambos campos técnicos; FileInfo/FileContent no los exponen.
Las firmas de IMetadataRepository e IStorageProvider se conservan. UpdateAsync
ahora requiere la revisión del snapshot leído y la incrementa en ese objeto al
confirmar éxito. Un conflicto produce InvalidOperationException, no un upsert.

La nueva capacidad opcional IRecoveryMetadataRepository extiende la abstracción
existente con TryRecordUploadAsync, TryActivateRecoveredAsync y GetByStorageKeyAsync.
SqlServerMetadataRepository la implementa. Las dos primeras operaciones devuelven
false por conflicto y actualizan el snapshot al confirmar éxito. Son operaciones
técnicas: la primera solo debe llamarse tras Save exitoso, y la segunda tras
verificar íntegramente contenido contra el recibo; no certifican archivos por sí solas.

ArkyvaService sigue aceptando IMetadataRepository: un proveedor externo sin esa
capacidad conserva el flujo anterior, sin recibo recuperable. Dicho proveedor debe
implementar su propia concurrencia revisionada para ofrecer garantías equivalentes.
StorageRecoveryService exige expresamente IRecoveryMetadataRepository. No se
introdujo otra interfaz de servicio porque una clase concreta con RecoverAsync basta.

## Ciclo de carga y fallos

La clave y FileId se generan una sola vez por UploadAsync; no hay reintentos.

| Etapa | Acción confirmada | Si falla o se interrumpe |
| --- | --- | --- |
| Insertar | Pending, tamaño provisional 0, sin recibo, revisión 0 | No se inicia Save; puede existir Pending si SQL confirmó tarde |
| Escribir/publicar | Save termina y se observa EOF; se cuenta tamaño y SHA-256 durante streaming | Se intenta Pending → Failed con la revisión original, sin borrar contenido |
| Guardar recibo | Se persisten tamaño real y hash en Pending, revisión +1 | Se conserva contenido; puede existir recibo si SQL confirmó tarde |
| Activar | Pending → Active con la revisión/recibo exactos, revisión +1 | Se conserva contenido y estado; recuperación podrá verificar el recibo |

El stream contador no requiere Length/Seek y no posee el stream del llamador.
Un proveedor que termina Save antes de consumir hasta EOF no puede producir Active
ni recibo. Un error después de publicar el definitivo pero antes de confirmar Save
NO genera recibo: aunque exista contenido, el resultado sigue siendo incierto y
se informa sin activarlo. Si el marcado Failed también falla, se conservan ambas
causas en AggregateException, igual que en la Fase 4.

El recibo afirma que un Save conforme al contrato terminó; no es una firma digital
ni prueba contra administradores hostiles. SHA-256 se calcula con buffers acotados
sobre los bytes consumidos. No se vuelve a descargar durante una carga normal.
La recuperación sí lee todo el definitivo y compara tamaño y hash; su coste es una
lectura adicional completa y CPU proporcional al contenido. No hay deduplicación.
Una caída en el pequeño intervalo entre Save y el recibo sigue siendo irrecuperable
automáticamente: es una limitación intencional para no confundir existencia con integridad.

## Estados y compare-and-swap

| Operación | Transiciones |
| --- | --- |
| UpdateAsync ordinario | Pending → Failed/Deleted; Pending con recibo → Active; Active → Failed/Deleted; Failed → Deleted; mismo estado con campos coherentes |
| Recuperación verificada | Pending o Failed con recibo → Active |
| Deleted | Terminal; solo actualización del mismo estado y misma fecha de eliminación |

Cada UPDATE comprueba FileId y Revision leída, además de las condiciones de estado
y datos inmutables. Incrementa Revision y comprueba filas afectadas. Solo una
operación puede ganar para una revisión. Un segundo recuperador, borrado o escritor
con snapshot atrasado recibe false/conflicto y no sustituye el resultado ganador.
Las operaciones ordinarias no permiten Failed → Active. Tamaño y recibo se fijan
solo por TryRecordUploadAsync mientras Pending sin recibo; luego son inmutables.
No hay bloqueos estáticos, leases con expiración ni una transacción entre SQL y disco.

Un borrado concurrente puede ganar a una carga/recuperación; no se reactiva Deleted.
DeleteAsync conserva su consulta única de confirmación para aceptar otro borrado ya
persistido, pero no repite una escritura que haya perdido frente a otro estado.
La aplicación puede decidir reintentar con un snapshot nuevo según el caso.

## Uso manual y DryRun

```csharp
var storage = new FileSystemStorageProvider(baseDirectory);
var metadata = new SqlServerMetadataRepository(connectionString);
var recovery = new StorageRecoveryService(storage, metadata, "FileSystem");

var preview = await recovery.RecoverAsync(new RecoveryOptions
{
    MinimumAge = TimeSpan.FromHours(2),
    DryRun = true,                 // Valor predeterminado
    PageSize = 100,
    MaximumRecords = 1000,
    MaximumStorageEntries = 1000,
    InspectStorage = true
});

// Ejecutar explícitamente después de evaluar las observaciones:
var applied = await recovery.RecoverAsync(new RecoveryOptions
{
    MinimumAge = TimeSpan.FromHours(2),
    DryRun = false
});
```

DryRun lee metadatos y contenido para producir ProposedRecoveries/ProposedFailures;
no modifica estados, recibos, revisiones ni archivos. La ejecución posterior vuelve
a verificar y puede obtener otro resultado si hay cambios concurrentes.

Se examinan registros con CreatedUtc anterior al corte UTC capturado al inicio.
Se consulta por todos los estados, orden estable y paginación, para que cambiar
Pending a Active no desplace los demás candidatos. Las consultas son snapshots
separados; no se garantiza una instantánea global frente a inserciones antiguas o
cambios externos. Hay deduplicación local de IDs, presupuesto de páginas y límites
de registros/entradas. Truncated exige otra ejecución explícita o revisar límites.
El límite se aplica a registros de todos los proveedores, no solo al configurado.

## Decisiones de recuperación

| Observación | Acción |
| --- | --- |
| Pending/Failed antiguo, sin recibo, con o sin contenido | Omitir por incertidumbre; no activar ni declarar abandonado |
| Pending/Failed con recibo, tamaño y hash válidos | Proponer o intentar activación revisionada |
| Pending con recibo y definitivo ausente o incompatible | Proponer o intentar Failed con revisión exacta |
| Failed con recibo ausente/incompatible | Conservar Failed y diagnosticar |
| Error de acceso/red/lectura | Registrar error seguro; no declarar contenido corrupto ni cambiar estado |
| Active cuyo contenido falta | Diagnóstico ActiveContentMissing, sin degradación automática |
| Deleted con contenido | Diagnóstico DeletedContentRetained, sin borrado |
| Proveedor diferente | Omitir sin consultar su contenido mediante el proveedor equivocado |
| Conflicto de revisión | Omitir; otra operación pudo completar o borrar el registro |

Un Pending sin recibo podría ser una carga muy larga aún activa, aunque tenga días:
la edad no autoriza marcarlo Failed. Cuando existe recibo, la escritura ya terminó
para esa revisión. Las verificaciones fallidas conservan toda la referencia técnica
y el recibo para diagnóstico; no se elimina contenido definitivo en ningún caso.
Los hashes de archivos Active no se releen sistemáticamente: esta versión diagnostica
su ausencia, pero no implementa una auditoría de integridad de todo el repositorio.

## Temporales y contenido sin referencia

FileSystem añade IStorageDiagnostics.InspectAsync: inventario acotado de solo
lectura. Reutiliza su validación de enlaces y omite reparse points; los errores se
cuentan sin publicar rutas. Reconoce exactamente .arkyva-<32 hex minúsculos>.tmp.
Los demás nombres se consideran contenido de referencia desconocida, nunca basura.

Recovery identifica temporales recientes y antiguos. CleanTemporaries permite
expresar una solicitud de limpieza, pero esta versión la rechaza conservadoramente
con CleanupRequiresOwnershipEvidence y conserva los archivos: no existe una relación
fiable entre temporal, propietario y lease multiproceso. TemporariesDeleted siempre
es cero. Tampoco un temporal antiguo y desbloqueado prueba abandono: Save cierra el
stream justo antes del rename y podría estar en ese intervalo o en una espera SMB.
El escritor sigue limpiando su PROPIO temporal ante errores controlados como antes.

No se ha implementado un borrado de mantenimiento que dependa de promesas locales
de inactividad. Haría falta un protocolo de propiedad o una ventana administrativa
que garantice ausencia de escritores en todas las instancias antes de permitirlo.

Para archivos definitivos se busca proveedor/clave exactos sin filtrar por edad ni
estado. Una consulta sin registro solo produce UnreferencedContentUncertain: podría
haber registro en tránsito, restauración o diferencias de identidad/case del proveedor.
No se deduce orfandad ni se elimina nada. Las reglas de equivalencia físicas de SMB
pueden diferir de la comparación binaria SQL; se siguen exigiendo claves canónicas.

## Resultado y diagnóstico

RecoveryResult contiene RecordsExamined, RecordsRecovered, RecordsMarkedFailed,
ProposedRecoveries, ProposedFailures, TemporariesIdentified, TemporariesDeleted,
OperationsSkipped, Errors, Truncated y Findings. Los contadores de acciones reales
no incluyen propuestas de DryRun. Findings contiene códigos estables, FileId si se
conoce, una huella opaca de clave para inventario y solo el nombre de tipo de error.
No contiene claves, rutas, credenciales, mensajes de excepción ni contenido binario.
Los resultados pueden integrarse con el logger de la aplicación sin un sistema nuevo.
Errores de configuración producen ArgumentException; fallos durante mantenimiento
se acumulan por registro/entrada o detienen el listado con un hallazgo Error.
Un error de confirmación SQL puede haber aplicado su cambio: el resultado informa
error y una nueva ejecución determinará el estado, sin contabilizar éxito no confirmado.

## Límites de SQL, SMB e integridad

SQL Server y FileSystem no son atómicos conjuntamente. Los cortes de red pueden
producir resultados inciertos y falsos negativos temporales del almacenamiento.
Una prueba de tamaño/hash necesita permisos de lectura y leer todo el archivo.
El árbol físico debe protegerse contra modificaciones externas: otro actor puede
cambiarlo después de verificarlo y antes/después de activar; SQL no bloquea disco.
SMB no garantiza aquí durabilidad ante cortes de energía o fallos del servidor.
No hay reintentos ocultos, limpieza periódica, autenticación nueva, compresión ni cifrado.
Los streams ya abiertos no se revocan tras eliminación lógica.

## Pruebas y resultados reales

```powershell
dotnet build Arkyva.sln --configuration Release
& ./tests/Arkyva.RecoveryTests/bin/Release/net472/Arkyva.RecoveryTests.exe
& ./tests/Arkyva.ServiceTests/bin/Release/net472/Arkyva.ServiceTests.exe
& ./tests/Arkyva.ContractTests/bin/Release/net472/Arkyva.ContractTests.exe
& ./tests/Arkyva.FileSystemTests/bin/Release/net472/Arkyva.FileSystemTests.exe
& ./tests/Arkyva.SqlServerTests/bin/Release/net472/Arkyva.SqlServerTests.exe
```

Las pruebas nuevas incluyen recibo SHA-256 de vector conocido, interrupciones antes
y después de confirmar recibo, activación fallida, Pending/Failed verificables,
contenido ausente/incompleto/no verificable, DryRun, repetición, paginación,
dos recuperadores simultáneos con barrera, borrado concurrente, revisión atrasada,
carga aún activa, inventario local con temporal abierto y definitivo desconocido,
y recuperación contra FileSystem real con metadatos falsos. No prueban SQL mediante
un sustituto en memoria: las pruebas SQL preparadas requieren un servidor real.

La suite SQL añade instalación repetida de V004, recibo/revisión, conflictos de
recuperación y de actualizaciones y búsqueda exacta de inventario. Sin
ARKYVA_SQL_TEST_CONNECTION, la integración sigue mostrando SKIP y no se considera
validada. No se ha probado un servidor SMB real ni cortes de energía/red reales.

Resultado ejecutado de esta fase: compilación Release de los ocho proyectos con
cero errores y cero advertencias; 26 escenarios de recuperación, 41 del servicio,
12 comprobaciones de contratos, 21 FileSystem y 63 validaciones SQL offline
satisfactorias. La integración SQL se omitió explícitamente por falta de conexión
configurada. SPECIFICATION.md permanece sin cambios y no se implementó la Fase 6.
