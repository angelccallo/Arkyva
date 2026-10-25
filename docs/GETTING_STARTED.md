# Inicio rápido e integración

## Requisitos y referencias

Se requiere SDK .NET para compilar, Windows con Developer Pack .NET Framework
4.7.2 para el ejemplo/pruebas net472 y un runtime .NET Framework compatible para
ejecutarlos. Las cuatro bibliotecas incluyen net472 y netstandard2.0. Para las operaciones
reales se necesitan SQL Server preparado y una ruta local o UNC accesible.

Desde NuGet, instalar únicamente Arkyva. ArkyvaHost se conserva desde preview.2;
el soporte nativo Framework se incorpora en preview.3 y la API síncrona en preview.4. Instalar la nueva versión
pública solo después de comprobar su publicación:

```powershell
Install-Package Arkyva -Version 1.0.0-preview.4
```

Para trabajar con el código de este repositorio, agregar UNA referencia al proyecto de composición:

```xml
<ItemGroup>
  <ProjectReference Include="ruta/al/repositorio/src/Arkyva/Arkyva.csproj" />
</ItemGroup>
```

No es necesario referenciar ni construir tipos concretos de proveedores desde el
código consumidor. El proyecto Arkyva referencia transitivamente Core, FileSystem y
SqlServer. En net472 no incorpora dependencias NuGet externas: usa System.Data del
Framework. En netstandard2.0 conserva Microsoft.Data.SqlClient 6.1.7 y Pkcs 8.0.2,
con sus transitivas. Los paquetes locales son solo validación previa; la publicación
pública de preview.4 es manual y está pendiente. No instalar System.Data.SqlClient
como paquete para obtener el proveedor incluido en Framework.
Véase [PACKAGING.md](PACKAGING.md) para instalación sin referencias a proyectos.

```powershell
dotnet build Arkyva.sln --configuration Release
```

## Preparar SQL Server

En la base de destino ejecutar explícitamente, en orden:

1. V001_CreateSchema.sql
2. V002_CreateFilesTable.sql
3. V003_CreateIndexes.sql
4. V004_AddRecoveryEvidence.sql

Están en src/Arkyva.Metadata.SqlServer/Scripts. Véase [SQLSERVER.md](SQLSERVER.md).
Build no crea bases, no abre conexiones, no aplica scripts y no comprueba el esquema.
Para migrar de una versión anterior, detener los escritores y actualizar todas las
instancias antes de reanudar; V004 introduce la revisión optimista.
La cuenta de ejecución necesita CONNECT, SELECT, INSERT y UPDATE sobre arkyva.Files;
la instalación se realiza con una identidad que tenga permisos DDL apropiados.
No se requieren permisos de crear bases durante el uso normal.

## Configuración fluida

Para una aplicación con una sola configuración se recomienda ArkyvaHost:

```csharp
using System;
using Arkyva;

ArkyvaHost.Configure(options =>
{
    options.UseSqlServer(connectionString);
    options.UseFileSystem(baseDirectory);
});

// En cualquier método asíncrono después de configurar:
Guid id = await ArkyvaHost.Current.UploadAsync(stream, "archivo.pdf");
```

Configure usa un ArkyvaHostOptions de alcance limitado al callback: conserva
internamente cada builder inmutable devuelto por Use y delega Build al builder
existente. No duplica validaciones ni operaciones. Use devuelve las mismas options
para encadenar opcionalmente. El callback debe terminar síncronamente: no usar
async void, recursión, ni esperar trabajo que llame otra vez a Configure.

Una sola configuración exitosa por AppDomain; llamadas concurrentes se serializan
y solo una ejecuta el callback si tiene éxito. La segunda lanza InvalidOperationException
sin construir proveedores. Current no espera el arranque: lanza InvalidOperationException
si Configure no terminó con éxito. Tras callback/Build fallido se conserva la excepción
original y se permite reintentar; no se deshacen carpetas que Build haya creado.
Las options capturadas quedan cerradas al finalizar el intento. No hay Reset público.

Para DI, pruebas o múltiples instancias independientes, conservar la API tradicional:

```csharp
using Arkyva;
using Arkyva.Core.Interfaces;

IArkyvaService arkyva = ArkyvaBuilder.Create()
    .UseSqlServer(connectionString)
    .UseFileSystem(baseDirectory)
    .Build();
```

Se eligió ArkyvaBuilder en un proyecto de composición separado para mantener Core
independiente de proveedores. Cada llamada Use devuelve otro builder inmutable;
se debe encadenar o conservar el valor devuelto. El orden UseSqlServer/UseFileSystem
es indistinto. Configuraciones derivadas y Build repetidos no comparten estado
mutable del builder. Dos servicios configurados con la misma base/ruta siguen
operando sobre el mismo repositorio físico y SQL; son objetos independientes, no
repositorios aislados. Para instalaciones distintas configurar recursos distintos.

UseSqlServer valida sintaxis, Data Source e Initial Catalog sin conectar. UseFileSystem
rechaza valores vacíos; el proveedor valida la ruta absoluta, enlaces y acceso en
Build. Si falta una configuración, Build lanza InvalidOperationException antes de
crear la carpeta. Los valores inválidos producen ArgumentException; permisos o E/S
se propagan desde FileSystem. Una configuración que construye correctamente no
certifica conectividad SQL, credenciales, esquema ni todos los permisos futuros.

Build construye SqlServerMetadataRepository, FileSystemStorageProvider y el
ArkyvaService existente con identidad estable FileSystem. No reimplementa sus
operaciones ni las expone mediante otra fachada de métodos duplicados.

## Rutas locales, UNC y permisos

```csharp
var local = ArkyvaBuilder.Create()
    .UseSqlServer(connectionString)
    .UseFileSystem(@"D:\Arkyva\Files")
    .Build();

var shared = ArkyvaBuilder.Create()
    .UseSqlServer(connectionString)
    .UseFileSystem(@"\\FILE-SERVER\Documentos")
    .Build();
```

Build puede crear la carpeta base. La cuenta que ejecuta la aplicación debe poder
recorrer directorios, crear subcarpetas y archivos, leer, escribir, renombrar y borrar
sus temporales. En SMB se necesitan permisos tanto del recurso compartido como
NTFS. No se agregan credenciales SMB, impersonación ni elevación en el builder.
No utilizar unidades mapeadas dependientes de una sesión interactiva para IIS.
Las garantías y límites de SMB siguen descritos en [FILESYSTEM.md](FILESYSTEM.md)
y [RECOVERY.md](RECOVERY.md).

## Web.config y ASP.NET Framework

La aplicación, no Core ni el builder, lee su configuración. El ejemplo siguiente
no contiene credenciales; sustituir servidor/base/ruta con valores del despliegue.
Si se usa autenticación SQL, gestionar el secreto mediante la configuración segura
de la aplicación y mantenerlo fuera del repositorio.

```xml
<configuration>
  <connectionStrings>
    <add name="Arkyva"
         connectionString="Data Source=SQL-SERVER;Initial Catalog=Arkyva;Integrated Security=True;Encrypt=True"
         providerName="System.Data.SqlClient" />
  </connectionStrings>
  <appSettings>
    <add key="Arkyva:StoragePath" value="\\FILE-SERVER\Documentos" />
  </appSettings>
</configuration>
```

```csharp
using System.Configuration; // Solo en la aplicación .NET Framework.
using Arkyva;
using Arkyva.Core.Interfaces;

string connectionString = ConfigurationManager.ConnectionStrings["Arkyva"]?.ConnectionString;
string storagePath = ConfigurationManager.AppSettings["Arkyva:StoragePath"];
IArkyvaService service = ArkyvaBuilder.Create()
    .UseSqlServer(connectionString)
    .UseFileSystem(storagePath)
    .Build();
```

providerName documenta el driver del target net472. Para netstandard2.0 corresponde
Microsoft.Data.SqlClient. El builder recibe una cadena y utiliza el proveedor del
ensamblado seleccionado; no usa DbProviderFactories ni necesita registrar
un factory. Tampoco depende de System.Web, IIS ni System.Configuration.

Especificar Encrypt=True;TrustServerCertificate=False en producción y preparar un
certificado SQL confiable para el nombre utilizado. Si Encrypt se omite, Arkyva
lo activa también en net472 para conservar el default seguro anterior. Las elecciones
explícitas existentes no se cambian para reducir dependencias; no usar Encrypt=False
ni TrustServerCertificate=True como solución general a errores de conexión.
Strict y HostNameInCertificate no se degradan automáticamente: el target net472
los rechaza. Si tu instalación los necesita, no actualizar sin una estrategia
compatible revisada. Ver [DEPENDENCIES.md](DEPENDENCIES.md) para mantenimiento,
características no equivalentes, excepciones y medición antes/después.

## ArkyvaHost en ASP.NET y WCF

En Global.asax.cs, integrar en el Application_Start existente. La aplicación lee
Web.config; Arkyva no depende de System.Configuration, System.Web ni WCF:

```csharp
using System;
using System.Configuration;
using Arkyva;

protected void Application_Start(object sender, EventArgs e)
{
    string connectionString =
        ConfigurationManager.ConnectionStrings["Arkyva"]?.ConnectionString;
    string storagePath = ConfigurationManager.AppSettings["Arkyva:StoragePath"];

    ArkyvaHost.Configure(options =>
    {
        options.UseSqlServer(connectionString);
        options.UseFileSystem(storagePath);
    });
}
```

En la implementación de una operación WCF existente, después de validar y autorizar
la solicitud y abrir su stream:

```csharp
Guid fileId = await ArkyvaHost.Current.UploadAsync(stream, nombreArchivo);
// Incorporar fileId a la respuesta del contrato existente.
```

No requiere acceder a Global ni HttpContext.Current. No llamar Configure por solicitud
ni desde cada constructor WCF. Verificar el arranque del hosting WCF concreto; si ya
utiliza ServiceHostFactory u otro punto de composición, configurar allí una única vez.
No cambiar el modo de compatibilidad ASP.NET ni contratos WCF solo por esta fachada.
En consola/servicio Windows, Configure se llama desde Main/arranque del servicio.

Current mantiene una instancia compartida por AppDomain, no entre procesos. Durante
un reciclado IIS puede haber dominios/procesos superpuestos, cada uno con su servicio;
la protección entre procesos sigue en FileSystem/SQL. El nuevo dominio puede configurar
otra vez. No requiere IDisposable ni Application_End; cada FileContent sí debe liberarse.
Un reciclado puede interrumpir cargas; no hay recuperación automática al iniciar.

Las pruebas que necesiten distintas configuraciones deben preferir inyectar un servicio
construido por ArkyvaBuilder o fakes de IArkyvaService. Para probar el estado estático
de ArkyvaHost usar procesos/AppDomains aislados, no reflexión ni Reset en producción.

## Ciclo de vida e inyección de dependencias

Construir una instancia por configuración al iniciar la aplicación o desde su
composición existente y reutilizarla entre solicitudes. No hay conexiones SQL
permanentes ni streams compartidos en servicio/proveedores; cada operación crea y
libera sus recursos. Las escrituras concurrentes conservan GUID, publicación sin
sobrescritura y compare-and-swap SQL de las fases anteriores.

Builder y servicio no implementan IDisposable porque no poseen handles abiertos
que requieran liberación al terminar su vida. Las descargas sí requieren using.
Build puede llamarse varias veces y crea nuevos objetos; no es necesario hacerlo
por solicitud. No existe un singleton global obligatorio.
ArkyvaHost es la alternativa global opcional; ArkyvaBuilder sigue creando instancias
independientes aunque el host ya esté configurado.

Registrar el IArkyvaService ya construido mediante el mecanismo de instancia del
contenedor actual, con ciclo de vida de aplicación si corresponde, o pasarlo
manualmente a consumidores:

```csharp
public sealed class Documents
{
    private readonly IArkyvaService arkyva;
    public Documents(IArkyvaService arkyva) { this.arkyva = arkyva; }
}
// En la composición de la aplicación:
var documents = new Documents(service);
```

Ningún contenedor es requerido. El builder inmutable puede compartirse para
construir, aunque conservar el servicio ya construido es más directo.

En IIS, configurar permisos para la identidad real del application pool y la
identidad con la que accede al servidor SQL/SMB. Un SID local de application pool
no equivale por sí mismo a una cuenta remota; resolver los permisos según el modelo
de identidades del despliegue. Véase la [documentación oficial de identidades IIS](https://learn.microsoft.com/en-us/iis/manage/configuring-security/application-pool-identities).
Usar await en solicitudes asíncronas; no bloquear mediante Result o Wait. El reciclado
no ejecuta recuperación automática ni elimina contenido. El mantenimiento sigue
siendo una llamada manual de la aplicación o de su proceso administrativo existente.

## API síncrona nativa y API asíncrona

La configuración de ArkyvaHost y el ciclo de vida no cambian. Después del arranque,
un endpoint WCF síncrono puede usar directamente la instancia compartida, sin Global,
HttpContext, otra configuración ni cambios de contrato WCF:

```csharp
using System;
using System.IO;
using Arkyva;

// Dentro del endpoint síncrono existente; validar/autorizar antes la solicitud.
using (var stream = File.OpenRead("documento.pdf"))
{
    Guid fileId = ArkyvaHost.Current.Upload(stream, "documento.pdf");
    // Asignar fileId a la respuesta del contrato existente.
}
```

En un método asíncrono:

```csharp
using (var stream = File.OpenRead("documento.pdf"))
{
    Guid fileId = await ArkyvaHost.Current.UploadAsync(stream, "documento.pdf");
}
```

La capacidad ISynchronousArkyvaService ofrece:

```csharp
Guid Upload(Stream content, string fileName);
FileContent Download(Guid fileId);
Arkyva.Core.Models.FileInfo GetInfo(Guid fileId);
PagedResult<Arkyva.Core.Models.FileInfo> List(FileQuery query);
bool Exists(Guid fileId);
void Delete(Guid fileId);
```

Los tipos son los existentes de Arkyva.Core.Models; GetInfo devuelve null para
ausentes/no activos. GetInfo/List no abren contenido; Exists comprueba disponibilidad
y Delete sigue siendo lógico e idempotente. Download exige Active, devuelve Content
e Info y requiere Dispose/using igual que DownloadAsync:

```csharp
using (var file = ArkyvaHost.Current.Download(fileId))
using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write))
{
    file.Content.CopyTo(output);
}
```

El llamador conserva la propiedad de la entrada de Upload/UploadAsync. No devolver
un stream después de cerrar el FileContent propietario. Se conservan las mismas
excepciones de Arkyva y sus causas internas para diagnóstico protegido. Una carga
fallida con resultado incierto no se reintenta ciegamente ni borra definitivos.

### Compatibilidad de implementadores

Agregar miembros abstractos a IArkyvaService/IStorageProvider/IMetadataRepository
habría roto implementaciones personalizadas. Se conservaron esas interfaces y
IRecoveryMetadataRepository sin modificaciones y se añadieron capacidades opcionales:

- ISynchronousArkyvaService: seis métodos públicos nativos, además del contrato Async.
- ISynchronousStorageProvider: Save, Read, Exists y Delete.
- ISynchronousMetadataRepository: Add, Get, List, Exists y Update.
- ISynchronousRecoveryMetadataRepository: TryRecordUpload, TryActivateRecovered y
  GetByStorageKey, además de los contratos anteriores de metadatos/recuperación.

ArkyvaService y los proveedores incluidos implementan las capacidades correspondientes.
Current y Build siguen devolviendo IArkyvaService. El namespace `Arkyva` contiene
ArkyvaSynchronousExtensions (en Core): únicamente verifica la capacidad y despacha
a ella. No mantiene estado, no coordina operaciones ni convierte Task a resultado.
Por eso `using Arkyva` permite `Current.Upload(...)` con la configuración anterior.
Para reflexión/proxies o tipado explícito consultar ISynchronousArkyvaService; Upload
no es un miembro añadido por reflexión a la interfaz original IArkyvaService.

Las implementaciones Async anteriores no necesitan nuevos miembros. Para admitir
la modalidad nativa deben implementar las capacidades con E/S síncrona real; los
decoradores/proxies también deben exponerlas. Si faltan, se lanza NotSupportedException
en vez de bloquear Async. Upload verifica almacenamiento, metadatos y cualquier
capacidad de recibos existente **antes de registrar Pending**, para no omitir evidencia
ni iniciar una carga incompleta. Puede seguir usando la API Async sin esta capacidad.

Las rutas síncronas usan CopyTo/Flush y ADO.NET Open/ExecuteReader/ExecuteScalar/
ExecuteNonQuery. Las Async mantienen CopyToAsync/FlushAsync y ADO.NET asíncrono.
Comparten validaciones, claves, snapshots, comandos/condiciones SQL y parámetros;
Pending → publicación → recibo → Active y tratamiento de resultados inciertos son
equivalentes. Se puede subir con una modalidad y leer/eliminar con la otra. La
recuperación manual sigue siendo RecoverAsync y acepta recibos producidos por ambas.

La E/S síncrona bloquea el hilo de la solicitud, lo esperado en WCF tradicional.
La Async es recomendable para servidores con mucha concurrencia; no sustituirla
indiscriminadamente para evitar await. No hay Task.Run, Result, Wait o GetAwaiter
en las operaciones del producto. La selección SQL, el cifrado y dependencias por
target de preview.3 se conservan; no se requiere ninguna migración SQL nueva.

## Operaciones y manejo de streams

```csharp
using System;
using System.IO;
using Arkyva.Core.Models;

Guid id;
using (Stream input = File.OpenRead(inputPath))
    id = await arkyva.UploadAsync(input, Path.GetFileName(inputPath));

using (FileContent file = await arkyva.DownloadAsync(id))
using (Stream output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write))
{
    string originalName = file.Info.OriginalName;
    string mime = file.Info.MimeType;
    long size = file.Info.SizeBytes;
    await file.Content.CopyToAsync(output);
}

var info = await arkyva.GetInfoAsync(id);
var page = await arkyva.ListAsync(new FileQuery { PageNumber = 1, PageSize = 20 });
bool available = await arkyva.ExistsAsync(id);
await arkyva.DeleteAsync(id);
```

La entrada pertenece al llamador. FileContent posee el stream de descarga y debe
liberarse; los resultados no contienen StorageKey, proveedor ni rutas. MIME usa el
valor predeterminado application/octet-stream del servicio existente. GetInfo y
List no verifican contenido físico; Exists es una observación puntual. Delete es
lógico e idempotente, conserva el contenido y lo excluye del listado predeterminado.
Véase [SERVICE.md](SERVICE.md) y [RECOVERY.md](RECOVERY.md) para estados y fallos.

ArgumentException/InvalidOperationException indican configuración o entrada inválida.
Las operaciones distinguen ArkyvaFileNotFoundException, ArkyvaFileUnavailableException,
ArkyvaStorageException y ArkyvaMetadataException. Conservar FileId para diagnóstico
de cargas inciertas; no borrar físicamente ni repetir cargas a ciegas. Los mensajes
principales del servicio son seguros; InnerException/ToString pueden contener datos
sensibles y solo corresponden a diagnóstico protegido. La consola no imprime
cadenas de conexión ni excepciones originales.

## Ejecutar la consola net472

El proyecto samples/Arkyva.Samples.NetFramework referencia únicamente Arkyva, además
de la referencia de framework System.Configuration para leer App.config. No contiene
credenciales ni una ruta fija. Las variables de entorno tienen prioridad; si no
están definidas utiliza connectionStrings[Arkyva] y appSettings[Arkyva:StoragePath].
Valores vacíos se rechazan, no se sustituyen silenciosamente.

```powershell
# Configurar únicamente en el entorno de desarrollo/pruebas elegido:
$env:ARKYVA_CONNECTION_STRING = 'Data Source=SQL-SERVER;Initial Catalog=ArkyvaDemo;Integrated Security=True;Encrypt=True'
$env:ARKYVA_STORAGE_PATH = 'D:\Arkyva\Files'

& ./samples/Arkyva.Samples.NetFramework/bin/Release/net472/Arkyva.Samples.NetFramework.exe --check-config
& ./samples/Arkyva.Samples.NetFramework/bin/Release/net472/Arkyva.Samples.NetFramework.exe 'C:\Demo\documento.pdf' 'C:\Demo\descarga-nueva.pdf'
```

Preparar previamente la base/schema, entrada y directorio de salida. La salida debe
ser un archivo nuevo; no sobrescribe archivos existentes. La ejecución carga,
descarga, consulta, lista, verifica y elimina lógicamente el registro creado; el
contenido físico retenido permanece en el repositorio. Si falla una etapa tras la
carga, el FileId ya impreso permite administrarlo; el ejemplo no intenta limpiar
contenido de resultado incierto.

Para UNC, sustituir solo ARKYVA_STORAGE_PATH por \\FILE-SERVER\Documentos o su entrada
en App.config. El modo --help no necesita configuración. --check-config construye
el servicio y la carpeta, pero explícitamente NO verifica conexión/schema SQL y no
se considera una prueba de carga/descarga de extremo a extremo.

## Binding redirects y despliegue

Consola y pruebas net472 activan AutoGenerateBindingRedirects y
GenerateBindingRedirectsOutputType en sus csproj. MSBuild genera los redirects
necesarios en el .exe.config de salida. Se comprobó que el ejemplo los genera y
que puede cargar el builder/SqlClient para construir la configuración en el runtime
.NET Framework instalado. Distribuir el contenido completo de bin/Release/net472,
incluidos .exe.config y las dependencias nativas del driver SQL según arquitectura.
No copiar únicamente Arkyva.dll.

En una aplicación web, resolver los conflictos reales de ensamblados en su propio
Web.config; los redirects de la consola no sustituyen la configuración del host.
No se fijan versiones de redirects de ejemplo que puedan contradecir dependencias
de la aplicación existente. La [guía oficial de redirects automáticos](https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/how-to-enable-and-disable-automatic-binding-redirection)
distingue la generación para escritorio del tratamiento de aplicaciones web.
El driver conserva las opciones TLS de la conexión; usar certificados apropiados
para el servidor configurado, sin desactivar validaciones desde la librería.

## Pruebas aisladas y resultado verificable

```powershell
& ./tests/Arkyva.ConfigurationTests/bin/Release/net472/Arkyva.ConfigurationTests.exe
& ./tests/Arkyva.SqlServerTests/bin/Release/net472/Arkyva.SqlServerTests.exe
```

ConfigurationTests ejecuta once escenarios sin SQL: configuración válida, dependencias
faltantes, valores inválidos, proveedores construidos, orden de llamadas, inmutabilidad,
instancias independientes, Build concurrente, reutilización y DI manual. Las pruebas
sin servidor no afirman haber ejecutado operaciones SQL válidas; ejercitan el servicio
con IDs inválidos y comprobaciones estructurales para evitar conexiones reales.

La prueba E2E está dentro del runner SQL existente. Si se configura
ARKYVA_SQL_TEST_CONNECTION hacia un servidor exclusivo de pruebas, crea una base
ArkyvaTests_<guid>, ignora Initial Catalog de la conexión, aplica V001-V004 y utiliza
un directorio temporal Arkyva-e2e-<guid>. Comprueba Build, carga, hash/tamaño/revisión
SQL, bytes físicos, descarga, GetInfo, paginación, Exists y eliminación lógica
(incluida conservación física y repetición). También carga concurrentemente desde
la misma instancia. El teardown elimina SOLO la base y carpeta nuevas del runner;
no toca datos ni carpetas de producción. Requiere permisos administrativos de
pruebas para crear/eliminar su base, distintos de los permisos de ejecución normal.
Un fallo con conexión configurada es un fallo, no SKIP.

Sin esa variable se ejecutan las validaciones offline y se muestra SKIP para SQL
y E2E. Los ejecutables de pruebas no son descubribles mediante dotnet test.
La Fase 7 valida SQL/E2E local mediante SQL Server LocalDB; IIS y SMB real siguen pendientes.
Se confirmó compilación net472 y ejecución de configuración en el runtime .NET
Framework disponible; no se afirma que se probó una instalación exacta 4.7.2 aislada
ni todas las combinaciones de ensamblados de una aplicación ASP.NET existente.

La Fase 7 prepara y verifica paquetes locales; no los publica ni agrega dashboard.
Los contratos y esquema SQL se conservan. Véase el informe de preparación vigente.

Resultado real de Fase 6: Release de los once proyectos con cero errores y cero
advertencias; 11 escenarios de configuración, 26 de recuperación, 41 del servicio,
12 comprobaciones de contratos, 21 FileSystem y 63 validaciones SQL offline
satisfactorias. La consola ejecutó --help y --check-config con variables de entorno
y con App.config temporalmente configurado (restaurado al finalizar), sin contactar
SQL. Se observaron los binding redirects generados en el .exe.config. SQL y E2E
quedaron explícitamente omitidos por falta de ARKYVA_SQL_TEST_CONNECTION.
