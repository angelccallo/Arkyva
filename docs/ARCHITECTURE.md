# Arquitectura de Arkyva

## Alcance y dependencias

Las tres bibliotecas usan `netstandard2.0`. Core define los contratos y modelos,
sin referencias a los proveedores. FileSystem y SqlServer referencian unicamente
Core, nunca entre si. FileSystem implementa el proveedor físico desde la Fase 2.
SqlServer implementa persistencia ADO.NET desde la Fase 3; ArkyvaService los coordina mediante sus interfaces desde la Fase 4, sin
transacciones distribuidas; la Fase 5 añade recuperación manual verificada.

```text
Arkyva.Storage.FileSystem --> Arkyva.Core <-- Arkyva.Metadata.SqlServer
                                   ^
                      Arkyva.ContractTests (net472)
```

- `src/Arkyva.Core/Interfaces`: API del consumidor, almacenamiento y metadatos.
- `src/Arkyva.Core/Models`: informacion publica, registro tecnico, estado y paginacion.
- `src/Arkyva.Core/Services` y `Exceptions`: servicio principal, streams internos
  y excepciones públicas de almacenamiento, metadatos y disponibilidad.
- `src/Arkyva.Storage.FileSystem`: proveedor físico con publicación temporal segura.
- `src/Arkyva.Metadata.SqlServer/Repositories` y `Scripts`: repositorio ADO.NET y
  scripts versionados del esquema `arkyva`, aplicados explícitamente por el operador.
- `tests/Arkyva.ContractTests`: comprobaciones ejecutables de contratos y consumo desde net472.

## Contratos y decisiones

`IArkyvaService` conserva las seis operaciones solicitadas. Recibe streams y nombres
originales y devuelve GUID, FileContent desechable o metadatos públicos. `FileInfo` no expone claves,
rutas ni identificadores del proveedor. `FileMetadata` extiende esos datos para el
contrato de persistencia; no es el tipo devuelto por la API del consumidor.
La herencia permite reutilizar los campos; el servicio copia explícitamente a DTOs
públicos de tipo FileInfo para no exponer propiedades técnicas por serialización.

`IStorageProvider` conserva exactamente las cuatro firmas minimas de la especificacion.
Solo administra contenido fisico. Save no sobrescribe y unicamente publica contenido
completo. Delete fisico es idempotente. El llamador conserva la propiedad del stream
recibido y debe liberar los streams devueltos. Los errores del proveedor se propagan;
una lectura inexistente produce FileNotFoundException.

`IMetadataRepository` define insercion, consulta individual, listado, existencia y
actualizacion. Get permite consultar cualquier estado para la futura coordinacion;
Exists considera solo registros activos. List usa Active por defecto y admite
filtros explícitos de otros estados desde la Fase 3. Update no inserta registros ausentes.
La eliminación lógica se representa actualizando Status y DeletedUtc. No se define
una transaccion distribuida ni se garantiza atomicidad entre contenido y metadatos.
El servicio coordina las llamadas desde la Fase 4; la Fase 5 implementa recuperación
manual con recibo y revisión optimista. Las transiciones SQL se protegen mediante actualizaciones condicionadas.

Decisiones sencillas donde no habia una firma prescrita:

- GetInfo devuelve null para archivos ausentes o no activos; Download distingue
  inexistencia (FileNotFoundException derivada) de estado no disponible. Delete logico es idempotente y no implica borrado fisico.
- Paginas basadas en uno, tamano inicial 50; numeros o tamanos no positivos deberan
  rechazarse. Filtros opcionales de nombre original y extension exactos, combinados
  con AND y comparacion ordinal sensible a mayusculas. Orden estable ascendente
  por CreatedUtc y FileId. TotalCount cuenta todos los resultados antes de paginar.
- Pending es el estado inicial, seguido de Active, Deleted o Failed segun la operacion.
  Las transiciones están protegidas en SQL y se coordinan en el servicio; no hay recuperación automática.
- Modelos simples mutables. Las validaciones pertenecen a las futuras implementaciones;
  las propiedades no realizan E/S ni validacion automatica en esta fase.
- En la Fase 1, sin paquetes explicitos en bibliotecas. El SDK restaura el paquete implicito de
  .NET Standard 2.0. XML de documentacion y advertencias tratadas como errores.
- Pruebas mediante ejecutable net472 sin framework adicional: codigo de salida 1 ante
  cualquier fallo. Verifica contratos, aislamiento de datos publicos y valores iniciales.
  No es un proyecto descubrible por `dotnet test`.

## Verificacion

Requiere SDK .NET con soporte netstandard2.0 y, en Windows, Developer Pack de
.NET Framework 4.7.2 y su runtime compatible.

```powershell
dotnet build Arkyva.sln --configuration Release
& ./tests/Arkyva.ContractTests/bin/Release/net472/Arkyva.ContractTests.exe
```

El proyecto net472 referencia las tres bibliotecas y permite comprobar su consumo.
Las pruebas unitarias del servicio existen desde la Fase 4; la recuperación manual tiene pruebas desde la Fase 5; la integración SQL preparada en la Fase 3 requiere un servidor de pruebas;
no se conectan bases de datos ni se realizan operaciones de archivos en estas pruebas.
La aplicacion de ejemplo, API fluida y publicación de paquetes NuGet quedan para fases
posteriores. SPECIFICATION.md permanece sin cambios.

## Resultado de verificacion de la Fase 1

- Compilacion Release de los cuatro proyectos: cero errores y cero advertencias.
- Ejecutable de contratos net472: nueve comprobaciones satisfactorias.
- Sin operaciones de archivos, conexiones SQL ni pruebas de integracion simuladas.
- La restauracion inicial requirio acceso a NuGet fuera del entorno restringido.

## Fase 2: proveedor físico

Se implementó FileSystemStorageProvider sin modificar Core ni sus interfaces.
Las referencias permanecen iguales; no se añadieron paquetes externos.
El proyecto Arkyva.FileSystemTests (net472) referencia únicamente el proveedor.
La resolución de claves restringe segmentos y verifica contención; rechaza puntos
de reanálisis detectados. Las escrituras publican mediante File.Move sin reemplazo
tras cerrar un temporal exclusivo en el mismo directorio. No se usan bloqueos
estáticos. El árbol debe estar protegido contra cambios externos no confiables.

[Documentación del proveedor](FILESYSTEM.md): uso, excepciones, permisos,
pruebas y límites de atomicidad y durabilidad en SMB. Los metadatos y la coordinación
del servicio se implementan en las fases siguientes descritas abajo.

## Fase 3: persistencia de metadatos

SqlServerMetadataRepository implementa las cinco firmas existentes de
IMetadataRepository. No se añaden métodos equivalentes con nombres duplicados:
UpdateAsync representa también cambios de estado y eliminación lógica.

Ajuste mínimo justificado de Core: FileQuery añade FileId, Status y límites de
creación UTC; Status conserva Active por defecto y null solicita todos los estados.
Se actualiza la documentación de ListAsync para reflejarlo, sin cambiar su firma.
La descarga del consumidor permanece restringida a archivos activos; el listado
admite estados explícitos desde la Fase 4 y sigue siendo Active por defecto.

Microsoft.Data.SqlClient 6.1.7 es la única dependencia directa nueva del proveedor.
Cada operación libera conexión y comandos; el driver gestiona el pooling. La tabla
arkyva.Files conserva los nombres de Core, fechas datetime2(7) e identidad física
única mediante una huella calculada para admitir claves de 1000 caracteres.
No se agregan tablas de negocio ni un sistema de migraciones automáticas.

Las transiciones permitidas avanzan desde Pending a Active/Failed/Deleted, desde
Active a Failed/Deleted y desde Failed a Deleted; Deleted es terminal. Las
actualizaciones preservan identidad física y fecha de creación; desde la Fase 4
la Fase 5 fija tamaño y recibo durante Pending antes de activar, con revisión exacta. La
paginación usa una transacción corta Serializable para alinear total y elementos.
Los fallos SQL no se reintentan ni se coordinan con operaciones de archivos.

[Documentación SQL Server](SQLSERVER.md): instalación, uso, índices, limitaciones,
permisos y ejecución de la integración en una base nueva aislada. La compilación y
las 58 validaciones offline se ejecutaron; la integración SQL sigue pendiente por
falta de servidor configurado. No se modificó FileSystem ni SPECIFICATION.md.

## Fase 4: servicio principal

ArkyvaService implementa las seis operaciones mediante las interfaces existentes y
un identificador estable de proveedor configurado en el constructor. No referencia
paquetes SQL, System.IO.File ni los proveedores concretos.

Ajustes públicos mínimos justificados: DownloadAsync devuelve FileContent (Info y
Content, IDisposable) para incluir nombre, MIME y tamaño con el stream. ListAsync
admite estados explícitos y null para todos, conservando Active por defecto. No se
duplican DTOs ni interfaces. GetInfo conserva null para ausentes o no activos.

Upload crea Pending con tamaño provisional cero, transfiere mediante un stream
contador y fija tamaño/Active después de Save. SQL permite finalizar tamaño solo
en esa transición; fuera de ella sigue inmutable. No cambia el esquema ni FileSystem.
Un fallo de Save intenta Failed; un fallo de activación no provoca borrado físico
ni degradación que pueda contradecir una confirmación SQL tardía.

Las excepciones públicas tienen mensajes seguros y causas internas para diagnóstico.
Las descargas ocultan el stream concreto y liberan su recurso mediante FileContent.
Delete es lógico e idempotente, incluida confirmación de un borrado concurrente.
Ninguna operación del servicio borra contenido físico. La integración manual y los
flujos, límites de concurrencia y errores se detallan en [SERVICE.md](SERVICE.md).

## Fase 5: consistencia, recuperación y concurrencia

La decisión vigente se detalla en [RECOVERY.md](RECOVERY.md). Se conservan las
firmas existentes; FileMetadata añade Revision y CompletionSha256 técnicos.
IRecoveryMetadataRepository es una capacidad opcional para recibos, activación
verificada y búsqueda de referencias. SQL la implementa con UPDATE por revisión,
estado esperado y comprobación de filas afectadas. Las operaciones ordinarias
incrementan Revision y rechazan snapshots atrasados, incluso para cambios del
mismo estado: sustituye la política anterior de última escritura aceptada.

V004 añade columnas y restricciones de forma no destructiva. Debe aplicarse
manualmente con escritores detenidos y sin mezclar binarios de fases anteriores.
Los Active antiguos siguen legibles; no se inventa evidencia para cargas antiguas.

Upload calcula SHA-256 durante la lectura, exige EOF tras Save, registra recibo y
tamaño mientras Pending y luego activa. Fallos inciertos conservan contenido y
causas. Un repositorio externo sin la capacidad opcional mantiene el flujo previo,
sin recibo recuperable y con obligación propia de cumplir concurrencia equivalente.

StorageRecoveryService verifica tamaño/hash por streaming antes de intentar
Pending/Failed → Active. Un Pending con recibo inconsistente puede pasar a Failed;
sin recibo se omite porque podría seguir ejecutándose. No hay leases ni reintentos
que puedan reactivar Deleted. DryRun es predeterminado y no hace escrituras.

IStorageDiagnostics permite inventario físico acotado. La recuperación no elimina
archivos definitivos NI temporales: edad/desbloqueo no prueba abandono en todas
las instancias. Una solicitud CleanTemporaries genera diagnóstico de incertidumbre.
Es una limitación intencional permitida por el alcance conservador, no un limpiador
seguro implementado. El escritor conserva la limpieza de su propio temporal.

No se añadieron dependencias, tareas programadas ni procesos. Fase 6 no implementada.
