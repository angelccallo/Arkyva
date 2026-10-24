# Arquitectura de Arkyva

## Alcance y dependencias

Desde preview.3 (Fase 11) las cuatro bibliotecas compilan para net472 y netstandard2.0.
Las descripciones por fase de abajo conservan sus decisiones históricas. La API,
dependencias entre proyectos y ciclo de vida de ArkyvaHost permanecen iguales.
Solo el proveedor ADO.NET interno depende del target: Framework System.Data.SqlClient
en net472; Microsoft.Data.SqlClient 6.1.7/Pkcs 8.0.2 en netstandard2.0. No se agrega
otro proveedor de almacenamiento ni una abstracción pública para SqlClient.

Un #if NET472 selecciona el namespace interno; consultas, parámetros, async,
transacciones y revisión optimista se compilan desde el mismo código. El constructor
nativo conserva Encrypt=True cuando se omite, traduce Mandatory/Optional a sus
equivalentes booleanos y rechaza Strict sin degradación. No cambia SQL ni el esquema.
La reducción de dependencias se valida desde los paquetes instalados, no por asumir
que lib/net472 basta. Ver [DEPENDENCIES.md](DEPENDENCIES.md) para resultados y riesgos.

Las cuatro bibliotecas usan `netstandard2.0` (incluido el proyecto de composición Arkyva de la Fase 6). Core define los contratos y modelos,
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
La aplicación de ejemplo y API fluida se añaden en la Fase 6; la publicación NuGet
queda para una fase posterior. SPECIFICATION.md permanece sin cambios.

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

## Fase 6: composición y aplicación consumidora

Se añade src/Arkyva (netstandard2.0), con ArkyvaBuilder. El proyecto referencia Core,
Storage.FileSystem y Metadata.SqlServer. Core mantiene su independencia: no se le
agregan referencias a proveedores, System.Configuration ni System.Web.

ArkyvaBuilder.Create().UseSqlServer(...).UseFileSystem(...).Build() devuelve el
IArkyvaService existente. Sus campos son readonly y cada Use devuelve otro builder;
no hay estado global mutable. El constructor del repositorio valida SQL sin abrir
conexión. Build verifica configuraciones faltantes y crea el proveedor FileSystem,
que puede crear su carpeta base. No comprueba conectividad ni aplica migraciones.

Cada Build construye proveedores y servicio nuevos. El servicio puede reutilizarse
concurrentemente para una configuración; no tiene handles persistentes ni requiere
IDisposable. Las descargas siguen siendo desechables. La composición manual y los
contenedores existentes pueden registrar IArkyvaService por instancia, sin imponer
un singleton global ni una dependencia de DI.

samples/Arkyva.Samples.NetFramework es una consola net472 que referencia solo el
proyecto de composición; lee App.config o variables de entorno y demuestra las
seis operaciones. AutoGenerateBindingRedirects genera su configuración de salida.
El modo --check-config permite comprobar construcción/carga de ensamblados sin SQL,
y declara explícitamente que no probó conectividad ni contenido.

Arkyva.ConfigurationTests cubre configuración sin servidor. La integración E2E se
incorpora al runner SQL existente, reutilizando su base única y una carpeta temporal
nueva; no se duplica la infraestructura de creación/eliminación de bases. Si no hay
conexión de pruebas se informa SKIP. No hay cambios en contratos, servicio,
proveedores ni scripts SQL. Véase [GETTING_STARTED.md](GETTING_STARTED.md) para
Web.config, IIS, SMB, ejemplo y límites de la compatibilidad comprobada.

Verificación Fase 6: once proyectos compilados en Release sin errores/advertencias;
11 pruebas de configuración satisfactorias en net472 y suites anteriores exitosas.
La consola carga configuración desde entorno y App.config en el runtime disponible.
La integración SQL/E2E no se ejecutó sin conexión de pruebas; no se afirma haber
validado IIS, SMB real ni una instalación aislada del runtime 4.7.2 exacto.

## Fase 7: validación y distribución preview

Se distribuyen los cuatro proyectos existentes como paquetes 1.0.0-preview.1, sin
nuevas fachadas, cambios de contratos ni funcionalidades documentales. El proyecto
principal Arkyva depende de los tres paquetes de implementación/contratos existentes.
Los metadatos reales se centralizan en src/Directory.Build.props y todos incluyen
licencia MIT, README y documentación XML. La ruta empaquetada de scripts SQL se
corrigió para no duplicar Scripts. La dependencia transitiva Pkcs se fija a 8.0.2
por avisos de seguridad, sin modificar el comportamiento del servicio.

Las suites ejecutables se consolidan mediante scripts/Test.ps1; Audit.ps1 exige
consulta satisfactoria del feed y Test-Packages.ps1 inspecciona ZIP/manifiestos y
restaura un consumidor net472 con caché aislada fuera de su árbol de compilación.
Su única dependencia de producto es PackageReference Arkyva. Se valida también
contra SQL real dentro de la base nueva del runner, sin referencias a proyectos.
La Fase 7 añade al fixture la verificación de recuperación con SQL/FileSystem reales.

GitHub Actions prepara compilación, auditoría, pruebas y paquetes en Windows;
SQL externo es exclusivamente opt-in manual con entorno protegido. No hay publicación,
release ni push automático. La ejecución remota del workflow queda pendiente.
La validación SQL/E2E omitida en fases anteriores se ejecuta en esta fase mediante
LocalDB temporal aislado; los resultados y riesgos actuales están en
[RELEASE_READINESS.md](RELEASE_READINESS.md). El procedimiento de nuevos proveedores
sigue siendo implementar IStorageProvider, usar claves/streams y definir una identidad
estable; metadatos externos deben cumplir revisión y, opcionalmente, recibos mediante
IRecoveryMetadataRepository. Probar cada proveedor independientemente y componerlo
manualmente con ArkyvaService sin cambiar Core ni ampliar el builder para esta entrega.

Verificación vigente de Fase 7: 226 comprobaciones en seis suites y 14 internas en
el consumidor NuGet, con cero fallos; SQL Server LocalDB real, FileSystem local y
recuperación verificada. La instancia temporal se eliminó. GitHub Actions, IIS,
SMB y runtime 4.7.2 exacto no se presentan como validados. Ver informe para límites
funcionales pendientes y procedencia de los paquetes locales.

## Fase 8: publicación pública manual, aún no ejecutada

publish-nuget.yml solo admite workflow_dispatch y distingue validate de publish.
Ambos CI/workflow comparten Validate-Packages.ps1; no se duplica la implementación
de compilación/pruebas/empaquetado. La versión se proporciona explícitamente a MSBuild
y al consumidor temporal; se conserva 1.0.0-preview.1 como versión inicial.

La publicación requiere publish=true, repositorio/branch originales, environment
nuget-publishing aprobado y policy NuGet coincidente. Únicamente publish recibe
id-token:write y NuGet/login@v1 para angelccallo. Verifica hashes/commit, cuatro IDs
y ausencia de versiones existentes antes de obtener la credencial temporal; publica
Core, FileSystem, SqlServer y Arkyva, esperando indexación entre pasos. No ignora
conflictos, no cambia IDs, no solicita API Keys permanentes y no crea tags/releases.

No se modifica lógica de archivos, metadatos, recuperación, contratos ni esquema SQL.
La consulta pública no detectó versiones de los cuatro IDs, pero no reserva nombres.
La configuración de cuentas/policies y la primera ejecución pública permanecen
manuales y no se hicieron en esta tarea. Ver [NUGET_PUBLISHING.md](NUGET_PUBLISHING.md).

## Fase 10: ArkyvaHost opcional (1.0.0-preview.2)

ArkyvaHost vive en el ensamblado/namespace Arkyva y publica IArkyvaService mediante
Current. No cambia Core, ArkyvaBuilder, proveedores ni esquema SQL. Para una única
configuración por AppDomain ofrece Configure(Action<ArkyvaHostOptions>); el builder
tradicional sigue siendo la alternativa para DI, pruebas y múltiples instancias.

El callback usa options mutables de alcance limitado porque el builder existente
es inmutable. Cada Use delega al builder y conserva el nuevo valor, sin replicar
validaciones ni creación de proveedores. Options no expone Build y se cierra al
terminar el intento para impedir mutaciones posteriores sobre referencias capturadas.

Un lock privado serializa callback y Build. Solo después del éxito se publica con
Volatile.Write; Current utiliza Volatile.Read y falla claramente si aún no hay servicio.
El finally permite reintentar ante excepciones y rechaza recursión durante Configure.
No hay Reset, sustitución pública, espera activa ni bloqueo de las operaciones del servicio.
El callback es síncrono; no debe usar async void ni esperar otra llamada Configure.
El lock coordina únicamente inicialización dentro del AppDomain; no sustituye las
garantías entre procesos ya existentes en SQL y FileSystem.

En Application_Start (o arranque equivalente) configurar una vez y consumir Current
desde WCF sin Global/HttpContext. No se mantienen conexiones ni streams entre llamadas,
ni se requiere Dispose/Application_End. Un nuevo AppDomain de IIS puede configurar de
nuevo; no se ejecuta recuperación automática ni se promete terminar cargas interrumpidas.

Las pruebas del host corren cada escenario en un AppDomain nuevo y lo descargan
al terminar. Sin Reset ni alteración de campos privados; también verifican que los
servicios del builder tradicional no dependan del host. El consumidor NuGet net472
comprueba ambas APIs y usa Current en el flujo SQL/FileSystem de integración.
