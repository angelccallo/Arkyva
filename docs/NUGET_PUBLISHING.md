# Publicación oficial de Arkyva en NuGet.org

Estado de esta fase: mecanismo preparado y validado localmente; **no se autenticó
con NuGet/login, no se creó una política ni se publicó ningún paquete**.
El feed local es solo una prueba previa, no el mecanismo oficial de distribución.

## Identidad, paquetes y estado observado

Repositorio obtenido de git remote origin: https://github.com/angelccallo/Arkyva.git.
El default branch observado es main. Se conserva Authors=Angel Ccallo porque es una
atribución válida existente; el usuario de autenticación/propiedad NuGet es angelccallo.
MIT, descripción, tags, README, RepositoryType=git y URL reales permanecen configurados
para las cuatro bibliotecas en src/Directory.Build.props y sus csproj.

| PackageId | Primera versión | Depende de |
| --- | --- | --- |
| Arkyva.Core | 1.0.0-preview.1 | Sin proveedores concretos |
| Arkyva.Storage.FileSystem | 1.0.0-preview.1 | Arkyva.Core |
| Arkyva.Metadata.SqlServer | 1.0.0-preview.1 | Arkyva.Core, driver SQL y su restricción criptográfica de seguridad |
| Arkyva | 1.0.0-preview.1 | Los tres anteriores |

El consumidor instala únicamente Arkyva; NuGet resuelve los demás. No se empaquetan
pruebas, ejemplos ni símbolos. Las dependencias internas se validan contra la misma
versión solicitada y los manifiestos se inspeccionan antes de publicar.

La consulta pública de esta fase devolvió HTTP 404 en los cuatro índices de versiones:
no hay versiones visibles publicadas. La evidencia con fecha UTC está en
artifacts/nuget-availability.json. **404 no reserva el nombre ni prueba que la cuenta
pueda registrarlo**: puede existir una reserva o una carrera con otra publicación.
NuGet.org determina disponibilidad/permisos al aceptar el push.

El control falla si encuentra un ID ya existente en la primera publicación o una
versión ya publicada en cualquier publicación. Ante conflicto de Arkyva, detenerse:
comprobar propietarios, contactar con NuGet o evaluar otro prefijo/nombre mediante
una decisión explícita. No cambiar identificadores ni crear paquetes alternativos
automáticamente. Ninguna alternativa se declara disponible sin comprobarla.

## Configuración manual de Trusted Publishing

Seguir la [documentación oficial de NuGet](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
y [NuGet/login](https://github.com/NuGet/login). Estos pasos los realiza el propietario;
no fueron ejecutados por esta tarea.

1. Acceder a NuGet.org como angelccallo y completar requisitos de cuenta, correo y
   seguridad que solicite NuGet. Proteger la cuenta y sus métodos de recuperación.
2. En la cuenta, abrir Trusted Publishing y crear una política GitHub con estos valores:

| Campo | Valor exacto |
| --- | --- |
| NuGet user/owner | angelccallo |
| Repository Owner | angelccallo |
| Repository | Arkyva |
| Workflow File | publish-nuget.yml |
| Environment | nuget-publishing |

Workflow File contiene SOLO el nombre, no .github/workflows/. La ruta real es
.github/workflows/publish-nuget.yml. La política debe permitir los paquetes nuevos
y nuevas versiones correspondientes. Limitarla a los cuatro IDs cuando la interfaz
lo permita; si requiere un único patrón, Arkyva* cubre el principal y sus dependencias,
y el workflow mantiene además una lista exacta de cuatro IDs. No autorizar paquetes
ajenos ni cuentas no necesarias. Revisar la política si NuGet muestra activación
temporal: la documentación explica el periodo de activación inicial y su renovación.

3. En GitHub crear el environment nuget-publishing, restringir sus ramas a main y
   configurar revisores requeridos. Su aprobación debe revisar versión, commit,
   resultado de validate y los artefactos, especialmente la primera publicación.
4. Incorporar el workflow al main del repositorio original mediante commit/push/merge
   autorizados. Actualmente hay cambios locales de Fase 7 que se conservaron y deben
   incorporarse junto con lo aprobado de Fase 8. No se hizo push ni se creó un tag/release.
5. No crear NUGET_API_KEY permanente en GitHub. El workflow usa NuGet/login@v1 con
   user=angelccallo, y únicamente el job publish recibe id-token:write. El login ocurre
   tras todas las validaciones y después de aprobar el environment. El output temporal
   NUGET_API_KEY pasa solo al paso de push mediante entorno, sin imprimirlo.

La credencial temporal dura una hora según NuGet; se solicita justo antes del push.
Un token OIDC no se usa para varios intercambios. No se ha probado el intercambio
real: requiere la política, el workflow remoto y autorización de publicación.

## Primera ejecución: validar sin publicar

En Actions elegir **Publish Arkyva to NuGet.org (manual)** y Run workflow sobre main:

| Input | Valor para la primera validación |
| --- | --- |
| version | 1.0.0-preview.1 |
| publish | false |
| allow_existing_ids | false |
| allow_stable | false |

El workflow solo admite workflow_dispatch. No hay triggers push, pull_request,
release ni tags para publicación. publish=false construye y valida, pero no solicita
OIDC ni inicia el job publicador. Ejecutarlo primero así y revisar informes.
El archivo debe existir en el default branch para aparecer normalmente en Actions.

## Primera publicación: solo después de autorización expresa

Cuando el propietario haya configurado las políticas y autorizado la publicación,
repetir el workflow con la misma versión y publish=true; los otros flags siguen false.
Aprobar después el environment protegido al revisar el candidato concreto.
No ejecutar esta combinación durante la preparación actual.

El job validate:

1. Verifica versión, repositorio/main para cualquier solicitud de publicación,
   checkout limpio y ausencia de versiones/conflictos públicos.
2. Ejecuta scripts/Validate-Packages.ps1: restore, Release con warnings como errores,
   auditoría NuGet, controles de publicación, seis suites, pack e instalación en
   un consumidor limpio net472. No usa credenciales de publicación.
3. Comprueba los cuatro paquetes, MIT, README, XML/DLL netstandard2.0, scripts SQL,
   URL/autor, dependencias internas y commit exacto GITHUB_SHA en sus manifiestos.
4. Guarda hashes SHA-256 y sube solamente el candidato probado e informes como artefactos.

El job publish, separado y dependiente de validate:

1. Solo funciona en angelccallo/Arkyva, refs/heads/main, con publish=true y aprobación
   del environment nuget-publishing. No recibe un permiso de escritura al repositorio.
2. Descarga el candidato de la MISMA ejecución; no reconstruye paquetes tras aprobarlos.
3. Verifica exactamente cuatro archivos permitidos, versión y hashes contra el
   manifiesto producido por validate; reinspecciona metadatos/commit y consulta
   nuevamente NuGet.org. Un error de red o JSON no equivale a disponibilidad.
4. Solicita la credencial temporal mediante NuGet/login@v1.
5. Ejecuta scripts/Push-NuGet.ps1 con la lista cerrada, en este orden:
   Arkyva.Core, Arkyva.Storage.FileSystem, Arkyva.Metadata.SqlServer, Arkyva.

El comando utiliza --api-key desde el entorno temporal y el endpoint oficial
https://api.nuget.org/v3/index.json. No usa --skip-duplicate ni ignora códigos de error.
No imprime credenciales. El script también rechaza uso fuera del job GitHub original/main.
Hay una única cola de concurrencia de publicación y no se cancela un push en curso
para empezar otro automáticamente.

Tras cada push se espera a que la versión aparezca en el índice público antes de
continuar con dependientes: sondeo cada 10 segundos, máximo 10 minutos por paquete.
Una espera agotada detiene el flujo, no confirma éxito ni vuelve a subir a ciegas.
La validación/indexación puede tardar; el paquete podría haber sido aceptado aunque
el cliente reciba un error. No hay transacción atómica entre los cuatro paquetes.

## Pruebas SQL y validaciones críticas

El flujo de publicación por defecto ejecuta suites locales/offline y consumidor
sin SQL. Las omisiones SQL quedan explícitas en el informe. La Fase 7 ya ejecutó
SQL LocalDB y consumidor real; no se presenta como prueba nueva de cada versión.
Antes de estabilizar o de cambios que afecten persistencia, ejecutar además el job
sql-integration del CI existente con conexión de pruebas protegida y confirmar su
resultado para el mismo commit. No se añade una conexión a producción ni un secreto
SQL al nuevo job de validación. Sin esas verificaciones no se debe aprobar estabilidad.

No hay errores conocidos de compilación/ensamblados en el consumidor comprobado.
Persisten los límites de tamaño/tipos previstos sin implementar, IIS/SMB y runtime
4.7.2 exacto no aislados. Deben revisarse como parte de la autorización de preview;
no se solventan cambiando la versión a estable ni se ocultan en el workflow.

## Versionamiento y publicaciones posteriores

La versión solicitada pasa mediante -p:Version a restore/build/pack de toda la solución;
no modifica la lógica ni archivos fuente. La plantilla del consumidor local se actualiza
con esa misma versión en su copia temporal. No se deriva de tags ni se crea un release.

Se admite SemVer canónico en minúsculas, sin ceros iniciales numéricos ni metadata +build
(que no crea una identidad NuGet nueva). Ejemplos:

| Versión | Uso | Autorización adicional |
| --- | --- | --- |
| 1.0.0-preview.1 | Primera evaluación pública | publish=true solo después de autorización |
| 1.0.0-preview.2 | Nueva preview | allow_existing_ids=true tras comprobar propiedad |
| 1.0.0 | Primera estable validada | allow_stable=true y aprobación explícita de estabilidad |
| 1.0.1 | Corrección compatible | Mismas verificaciones; nunca reemplaza 1.0.0 |
| 1.1.0 | Evolución compatible | Mismas verificaciones y contrato semántico |

allow_existing_ids NO comprueba la propiedad mediante Authors: es una confirmación
manual de que angelccallo posee los IDs existentes. NuGet/OIDC aplican los permisos
reales. Aunque se marque, una versión ya existente falla siempre. No usar ese flag
para sortear un conflicto con otro propietario. allow_stable es falso por defecto;
no debe marcarse sin resolver los requisitos de estabilidad y validar el cambio.

Para cada versión: revisar compatibilidad/esquema, elegir versión nueva para los
cuatro paquetes, validar con publish=false, revisar el candidato y publicar mediante
la acción manual autorizada. No reutilizar una versión que ya fue aceptada, incluso
aunque esté deslistada. No se efectúan cambios automáticos a 1.0.0.

## Verificación e instalación pública

Después de un push autorizado, comprobar los cuatro índices de versiones, la galería
y los propietarios en NuGet.org. Confirmar 1.0.0-preview.1 y que angelccallo figura
como propietario, no solo como autor. La cuenta y reservas no se verifican con HTTP 404.

En un proyecto .NET Framework 4.7.2, con NuGet.org habilitado:

```powershell
Install-Package Arkyva -Version 1.0.0-preview.1 -Source https://api.nuget.org/v3/index.json
# CLI en un proyecto compatible existente:
dotnet add package Arkyva --version 1.0.0-preview.1 --source https://api.nuget.org/v3/index.json
```

En Visual Studio habilitar **Include prerelease** para encontrar previews.
Estos comandos públicos solo se podrán usar cuando se confirme la publicación;
actualmente se documentan como procedimiento futuro, no como instalación ya disponible.
No mantener el source mapping local de la Fase 7 para Arkyva si se quiere instalar
la versión pública: usar un NuGet.Config del consumidor con NuGet.org como fuente
del producto y dependencias. Puede existir retardo de replicación/caché después del push.

Ver [GETTING_STARTED.md](GETTING_STARTED.md) para builder, Web.config y permisos.
Preparar SQL V001–V004 manualmente desde el paquete Metadata.SqlServer (o el repositorio).
La aplicación administra conexión y ruta; solo necesita PackageReference a Arkyva.
Mantener binding redirects del host net472, distribuir DLL nativas del driver y usar
FileContent en using. El usuario final no debe conocer claves internas.

Para actualizar un consumidor, elegir una versión publicada concreta con Update-Package
Arkyva -Version <nueva-version> o dotnet add package con --version. Revisar las cuatro
resoluciones internas y sus cambios antes del despliegue.

## Publicación parcial o versión problemática

No existe rollback automático del conjunto. Si un push falla o agota indexación,
revisar Manage packages y los índices antes de actuar: podría haber tenido éxito.
Un reinicio sobre la misma versión se bloqueará si alguno ya se publicó. Elegir
una nueva versión coherente para los cuatro paquetes y aprobarla conscientemente;
no ocultar el conflicto con skip-duplicate ni cambiar IDs automáticamente.

Si una versión contiene errores, detener nuevas publicaciones, avisar a consumidores
y corregir en una versión nueva. La deslistación es una operación manual del propietario:
Manage packages → Published packages → Manage → Listing → versión → desmarcar
List in search results → Save. No se ejecuta automáticamente ni en esta tarea.
Deslistar no elimina binarios ni permite reutilizar versión: una instalación con
versión exacta puede seguir restaurándola. Véase la [política oficial](https://learn.microsoft.com/en-us/nuget/nuget-org/policies/deleting-packages).
Puede marcarse también como deprecated con una alternativa, tras decidirlo el propietario.

## Evolución del esquema SQL

No se crean migraciones nuevas en esta fase. Mantener scripts V001–V004 y aplicar
futuras migraciones versionadas/aditivas de forma explícita. Coordinar backup,
compatibilidad de lectores/escritores y ventana de mantenimiento; una actualización
del paquete no ejecuta SQL al iniciar. No mezclar escritores antiguos que ignoren
Revision. Cambios incompatibles requieren revisión de contrato/versión semántica,
no una migración destructiva automática.

## Evidencia de preparación de Fase 8

- Los cuatro índices públicos devolvieron 404; no se detectó conflicto visible.
- Build Release de once proyectos y consumidor net472: cero errores/advertencias.
- 174 comprobaciones de las seis suites, 28 controles de publicación y 7 del
  consumidor local satisfactorios. Auditoría NuGet sin avisos conocidos.
- Se generaron/inspeccionaron cuatro nupkg 1.0.0-preview.1 y se restauraron desde
  un feed temporal sin ProjectReference. El feed local es solo validación previa.
- SQL/E2E no se repitieron en esta fase: omitidos explícitamente en la ejecución
  local, con evidencia real previa de Fase 7 separada en RELEASE_READINESS.md.
- No se ejecutó login OIDC, push, política de cuenta, tag/release ni workflow remoto.
- La lógica de almacenamiento y persistencia permanece intacta. Los cambios locales
  previos de Fase 7 se conservaron; todavía requieren commit/revisión autorizados.

Pendientes del propietario: configurar política y environment protegidos, incorporar
los cambios aprobados a main, ejecutar primero publish=false y, solo después de la
autorización expresa de la primera publicación, ejecutar publish=true y verificar
la galería/instalación pública. No hay API Keys permanentes que crear o compartir.
