# Preparación para publicación — 1.0.0-preview.1

## Actualización Fase 12: 1.0.0-preview.4

API síncrona nativa y Async complementarias en los mismos cuatro paquetes y targets.
IArkyvaService/IStorageProvider/IMetadataRepository/IRecoveryMetadataRepository se
conservan para implementadores existentes; las nuevas capacidades y extensiones de
dispatch son aditivas. Current, Build y la configuración no cambian.

- Release de ambos targets sin errores/advertencias. Guardia de código de producto
  sin Task.Run, Result, Wait o GetAwaiter y sin llamadas Async en archivos síncronos.
- 319 comprobaciones nativas y 317 con ensamblados Standard en las seis suites con
  SQL LocalDB/FileSystem reales; 31 comprobaciones internas del consumidor NuGet.
  Misma cobertura en dos compilaciones, no 636 escenarios distintos.
- Cobertura nueva: EOF/hash, temporales, entradas inválidas, fallos antes/después de
  publicación/recibo/activación, resultados SQL inciertos, colisiones, cargas síncronas
  y mixtas concurrentes, CAS, recuperación de ambas modalidades y ownership de streams.
- Superficie pública comparada entre targets y preservación aditiva de preview.3:
  sin miembros anteriores eliminados/cambiados. Las interfaces Async originales no
  requieren nuevos miembros; se prueba también un implementador exclusivamente Async.
- Dependencias sin aumento: net472 cuatro paquetes Arkyva, cero externos/redirects;
  consumidor Standard 49 paquetes/45 externos. Ambos consumidores usan PackageReference
  al feed local, sin referencias fuente. Cifrado/driver por target y SQL sin cambios.
- Auditoría y 28 controles de publicación satisfactorios. No se auditó el estado de
  parches del SO/Framework mediante NuGet. Los paquetes locales no sustituyen el
  candidato del futuro commit de main validado en CI.

Entorno de ejecución: Framework 4.8.09032 con target net472; no runtime 4.7.2 aislado,
IIS/WCF desplegado, SMB ni TLS remoto de producción. La E/S síncrona ocupa el hilo;
Async sigue recomendada para escalabilidad. La recuperación continúa siendo manual
mediante RecoverAsync. No se publican paquetes ni se ejecutan operaciones Git de
escritura en esta fase. Ver GETTING_STARTED.md y CHANGELOG.md.

## Actualización Fase 11: 1.0.0-preview.3

Los cuatro paquetes ofrecen lib/net472 y lib/netstandard2.0, con grupos de dependencias
independientes. API pública comparada entre targets y con las DLL de preview.2: sin
diferencias. ArkyvaHost, esquema, consultas y claves físicas se conservan.

- Consumidor limpio net472: 45 → 4 paquetes, 41 → 0 externos, 11 → 0 redirects,
  sin advertencias NuGet. El smoke consumer incluye un contrato WCF de compilación,
  no un host IIS. Consumidor Standard independiente: 49 paquetes, mismo driver 6.1.7.
- Suites con SQL Server LocalDB/FileSystem reales: 252 comprobaciones nativas y
  250 contra los ensamblados Standard; consumidor NuGet nativo: 18 comprobaciones.
  Se incluyen tipos SQL, transacciones, concurrencia y recuperación. No son 502
  escenarios distintos: se ejecuta la misma cobertura sobre dos compilaciones.
- 28 controles de publicación y auditoría NuGet sin vulnerabilidades conocidas.
  Esta auditoría no valida los parches del Framework ni del SO.
- Instancia exclusiva ArkyvaMultiTargetValidation y bases de pruebas eliminadas.
- Framework 4.8.09032 con target/pack 4.7.2: no se validó runtime 4.7.2 aislado,
  IIS/WCF real, SMB ni TLS remoto/certificados de producción.
- Los primeros intentos detectaron problemas del runner/inspector y del pack de
  scripts SQL al pasar a multitargeting; se corrigieron y se repitieron las validaciones.
  Los scripts SQL se empaquetan una vez y no se modificaron.

La elección Framework SqlClient es acotada al legado: cifrado predeterminado preservado,
Strict rechazado sin degradación, conexiones modernas no equivalentes documentadas.
Revisar [DEPENDENCIES.md](DEPENDENCIES.md) antes de aprobar la actualización. Nuevas
dependencias o migraciones no se introducen. Preparada para revisión y validación remota,
no publicada; no se realizaron commits/pushes/merges/tags/releases.

## Actualización Fase 10: 1.0.0-preview.2

El informe siguiente conserva la evidencia histórica de preview.1. Para preview.2:

- Se añaden ArkyvaHost y ArkyvaHostOptions solo al paquete principal; el builder,
  IArkyvaService, proveedores y scripts SQL permanecen sin cambios.
- Release de once proyectos y consumidor independiente net472: cero errores/advertencias.
- 240 comprobaciones en seis suites, incluidas 14 nuevas del host aisladas por AppDomain;
  28 controles de publicación y 17 comprobaciones internas del consumidor NuGet.
  Cero suites fallidas. SQL Server LocalDB y FileSystem locales reales, incluyendo
  Upload/Download y eliminación lógica mediante Current desde el paquete instalado.
- Cuatro nupkg preview.2 inspeccionados: DLL/XML netstandard2.0, MIT, dependencias,
  README y scripts SQL. Restauración con una única PackageReference Arkyva y caché nueva.
- Auditoría NuGet sin vulnerabilidades conocidas. El intento restringido encontró
  TLS/NU1301; la repetición autorizada completó restore, auditoría y validaciones.
- La base de pruebas y la instancia exclusiva ArkyvaHostValidation se eliminaron.
- Los índices públicos muestran preview.1 y no preview.2; no hubo login ni push.
- Runtime de ejecución disponible: Framework 4.8.09032, objetivo net472. No se probó
  un runtime aislado 4.7.2, IIS/WCF desplegado ni SMB real. No hay nuevas dependencias.
- Falta commit/revisión y validación remota del futuro commit de main antes de una
  publicación manual autorizada; los artefactos locales no sustituyen ese candidato.

Los límites funcionales de la preview y las responsabilidades del consumidor descritos
abajo permanecen. No se crearon tags/releases ni se realizaron operaciones Git de escritura.

Fecha: 2026-10-23. Rama de trabajo: dev. Esta fase prepara artefactos; no publica,
crea tags/releases, modifica historial ni sube cambios al remoto.

## Dictamen

**Apta para evaluación técnica como preview; no declarar v1.0 estable ni lista para
producción sin revisar los límites indicados.** Los cuatro paquetes se construyeron,
inspeccionaron, instalaron en un consumidor nuevo net472 y ejecutaron con SQL Server
real y FileSystem. Antes de publicar externamente: aprobar/guardar cambios,
regenerar desde un commit limpio, ejecutar CI remoto y verificar titularidad de IDs
NuGet y permisos de publicación. No se comprobó reserva de esos nombres en NuGet.org.

La política configurable de tamaño máximo/tipos permitidos de SPECIFICATION.md sigue
sin implementación. Esta fase no la añade porque prohíbe funcionalidades nuevas;
se documenta como requisito pendiente para estabilización, sin modificar requisitos.
La aplicación debe imponer esos límites y autenticar/autorizar todas las operaciones.

## Alcance de la revisión

Se revisaron Core (interfaces, DTOs, errores, streams, servicio y recuperación),
FileSystem, SQL y V001–V004, builder, ejemplo y suites. Se conservaron contratos,
separación de proyectos y responsabilidades. No se detectó una necesidad de
refactorización funcional; los cambios de producto son metadatos de distribución,
la restricción de una dependencia transitiva y la ruta del contenido SQL empaquetado.
No se cambiaron operaciones del servicio, estado de recuperación, esquema ni APIs.

## Problemas concretos encontrados y resueltos

1. NuGet señaló System.Security.Cryptography.Pkcs 8.0.1, transitiva de SqlClient,
   por los avisos de DoS [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx)
   y [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7).
   Se exige 8.0.2 en el proveedor SQL. La auditoría posterior del grafo resuelto
   no encontró vulnerabilidades conocidas; no se suprimieron avisos.
2. El empaquetado anterior duplicaba Scripts/Scripts en contentFiles. Se fijó la
   ruta por archivo y se verificó que V001–V004 aparecen una sola vez en la ruta
   documentada contentFiles/any/any/Scripts.
3. El fixture inicial del consumidor limpio ubicaba su caché NuGet dentro del
   proyecto y provocaba tres advertencias MSB3277 por assets de otros frameworks.
   Se movió fuera de las fuentes, a un directorio hermano, y el consumidor volvió
   a compilar sin advertencias con -warnaserror. No se ocultaron conflictos.
4. SQL/E2E estaban pendientes de fases anteriores: se ejecutaron ahora con una
   instancia temporal LocalDB, base nueva por corrida y directorios temporales.
   Se añadió cobertura faltante de recuperación real (DryRun, activación y repetición)
   y ejecución del consumidor instalado, reutilizando el fixture SQL existente.

## Entorno y compilación

- Windows x64, SDK .NET 9.0.316 y MSBuild 17.14.
- Cuatro bibliotecas netstandard2.0, seis suites ejecutables net472 y consola net472.
- Release de once proyectos: **0 advertencias, 0 errores**, incluyendo -warnaserror.
- Consumidor limpio net472: **0 advertencias, 0 errores**, binding redirects generados.
- Ejecución en .NET Framework instalado: registro Release 533320, Version 4.8.09032.
  No equivale a haber probado un runtime 4.7.2 exacto aislado ni todos los hosts web.
- SQL Server LocalDB 15.0.4382.1 en una instancia temporal exclusiva. No se instalaron
  servicios Windows ni se tocaron bases existentes. La instancia se detuvo y eliminó
  después de las pruebas; cada base nueva se eliminó por finally del runner.

## Resultados de la ejecución final

Se ejecutó scripts/Test.ps1 -RunSqlIntegration con el ejecutable del consumidor
instalado mediante paquetes locales. Los contadores representan comprobaciones
(assertions/escenarios), no descubrimiento de un framework dotnet test.

| Suite | Comprobaciones aprobadas | Fallos |
| --- | ---: | ---: |
| ContractTests | 12 | 0 |
| FileSystemTests | 21 | 0 |
| ServiceTests | 41 | 0 |
| RecoveryTests | 26 | 0 |
| ConfigurationTests | 11 | 0 |
| SqlServerTests | 115 | 0 |

**Total del ejecutor: 226 aprobadas, 0 suites fallidas, seis suites completas.**
El proceso consumidor instalado realizó además **14 comprobaciones internas**
(7 de construcción/validación y 7 del flujo SQL/FileSystem); su código de salida
se comprueba también en la suite SQL. Esas 14 no se suman en el JSON del padre.
Las cuatro inspecciones de paquetes son validación adicional de artefactos.

En la ejecución final no se omitieron los grupos SQL ni E2E. En la ejecución de
smoke sin servidor se anunció SKIP y solo se ejecutaron sus 7 comprobaciones offline;
posteriormente se completó la ejecución integrada. Los defaults de CI/local siguen
omitiendo SQL hasta recibir configuración segura y la opción explícita.

La integración real comprobó:

- Instalación repetida de scripts, inserción, consulta, ausentes y restricciones SQL.
- SHA/tamaño/revisión, estados, actualizaciones condicionales, duplicados y concurrencia.
- Filtros exactos, paginación estable, UTC y entradas de inyección tratadas como datos.
- Builder → Upload → SQL + FileSystem → Download con contenido idéntico y DTO público.
- GetInfo, List, Exists, Delete lógico, rechazo posterior de descarga y retención física.
- Cargas concurrentes desde la misma instancia.
- Recuperación de publicación interrumpida con recibo: DryRun sin cambio, activación
  verificada y repetición segura contra SQL/FileSystem reales.
- El mismo ciclo público desde un ejecutable con SOLO PackageReference Arkyva.

Los escenarios de fallos de entrada/escritura, confirmaciones inciertas, EOF,
liberación de recursos, no sobrescritura entre procesos y preservación de temporales
se mantienen cubiertos por las suites existentes, sin duplicar esas pruebas.

## Paquetes generados e inspeccionados

Directorio: artifacts/packages. Los proyectos de pruebas/ejemplo son IsPackable=false;
la plantilla del consumidor es deliberadamente externa a la solución.

| PackageId | Versión | Entradas ZIP |
| --- | --- | ---: |
| Arkyva | 1.0.0-preview.1 | 8 |
| Arkyva.Core | 1.0.0-preview.1 | 8 |
| Arkyva.Storage.FileSystem | 1.0.0-preview.1 | 8 |
| Arkyva.Metadata.SqlServer | 1.0.0-preview.1 | 12 |

Cada paquete contiene solo su DLL/XML netstandard2.0, README, LICENSE y metadatos
NuGet estándar. SQL incluye además cuatro scripts. Se comprobaron dependencias de
preview, MIT, URL real, autor real y ausencia de fuentes, PDB, configuraciones,
secretos, temporales y directorios bin/obj. No se generaron snupkg ni otros paquetes.

La versión, licencia, autor (Angel Ccallo de Git), copyright (LICENSE) y URL origin
se centralizan en src/Directory.Build.props. El principal depende de los tres
paquetes de implementación/contratos de la misma preview. SQL depende directamente
de Microsoft.Data.SqlClient 6.1.7 y del suelo de seguridad Pkcs 8.0.2; el resto son
transitivas de esos paquetes. La auditoría incluye directas y transitivas.

El SDK registra HEAD como commit en los paquetes; actualmente es af17e81, anterior
al commit todavía no solicitado de esta fase. Por ello estos son artefactos locales
de validación, no paquetes para publicar tal cual: regenerar desde el commit limpio
aprobado para asegurar procedencia exacta. No se alteró el historial para resolverlo.

## Instalación limpia verificada

scripts/Test-Packages.ps1 copió tests/Arkyva.PackageSmoke a un directorio nuevo
ignorado. Su .csproj contiene una única referencia de producto: Arkyva 1.0.0-preview.1.
La caché es nueva y externa al proyecto. Source mapping obliga a restaurar Arkyva y
Arkyva.* desde el feed local y dependencias externas desde NuGet.org. Se inspeccionó
assets: los cuatro IDs son de tipo package, no project. Se compila, ejecuta Build y
se prueba la API pública. El runner SQL suministra exclusivamente su base temporal
para validar el flujo completo de ese consumidor, sin tipos concretos de proveedores.

Evidencia local reproducible, ignorada por Git:

- artifacts/test-results/summary.json: resultados de seis suites.
- artifacts/audit.json: consulta NuGet sin vulnerabilidades conocidas.
- artifacts/package-inspection.json: manifiestos inspeccionados y SHA-256 de nupkg.
- artifacts/package-smoke/latest.txt y su directorio: consumidor/restauración reales.

## Seguridad y repositorio

.gitignore mantiene reglas Visual Studio y excluye artifacts, bin/obj, paquetes y
archivos de usuario. Se revisaron archivos versionados y candidatos nuevos con
patrones de credenciales; no se encontraron secretos ni artefactos de compilación
incluidos por error. Es una comprobación estática acotada, no una certificación
exhaustiva. LICENSE y SPECIFICATION.md permanecen sin cambios. No hubo eliminación
de archivos legítimos, tags, releases, publicación ni push.

La autorización del consumidor, ACL de almacenamiento, TLS/credenciales SQL y
límites del host son obligatorios; ver [SECURITY.md](SECURITY.md). Los resultados
inciertos conservan contenido. No hay borrado de temporales sin propiedad fiable.

## GitHub Actions

Workflow preparado en .github/workflows/ci.yml, permisos contents:read, Windows,
restore/build/audit/suites/pack/instalación limpia y artefactos de validación.
Los comandos y scripts se ejecutaron localmente y los scripts PowerShell pasaron
parseo sintáctico; el YAML se revisó. **No se ejecutó en GitHub**: no se autorizó push.
La primera ejecución remota y configuración/protección del environment sql-integration
siguen pendientes. Su job SQL requiere workflow_dispatch y secreto explícitos;
no usa secretos desde pull requests ni publica a NuGet.org.

## Validaciones y riesgos pendientes

- Primera ejecución del workflow remoto y comportamiento del runner hospedado.
- Runtime 4.7.2 exacto aislado, otras versiones .NET y hosts IIS existentes.
- SMB real, fallos de red/energía, ACL remotas y otras autenticaciones SQL/Entra.
- Límite configurable de tamaño/tipos previsto para estabilización; no implementado.
- Confianza en el árbol físico: las comprobaciones portables no eliminan carreras
  con actores capaces de sustituir enlaces/directorios fuera de Arkyva.
- No hay auditoría de hash automática para todo Active ni recuperación de históricos
  sin evidencia suficiente. Temporales inciertos y definitivos se conservan.
- Verificar IDs NuGet/cuenta de publicación y reconstruir artefactos desde commit
  limpio antes de una publicación manual autorizada.

No se incorporaron dashboard, proveedores nuevos, versionamiento, autenticación
propia ni otras funcionalidades fuera del MVP.
