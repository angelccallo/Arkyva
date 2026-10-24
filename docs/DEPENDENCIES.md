# Targets, dependencias y SqlClient — preview.3

## Decisión y alcance

Los cuatro paquetes Arkyva, Arkyva.Core, Arkyva.Storage.FileSystem y
Arkyva.Metadata.SqlServer incluyen **net472 y netstandard2.0**. NuGet elige el grupo
más próximo al consumidor: un WCF net472 usa lib/net472, sin referencias manuales
a DLL ni dependencias de WCF dentro de Arkyva. No hay nuevos proveedores físicos,
integración específica ASP.NET Core, cambios de API, host, esquema ni StorageKey.

| Target | Proveedor SQL interno | Dependencias externas declaradas por Arkyva |
| --- | --- | --- |
| net472 | System.Data.SqlClient de System.Data del Framework | Ninguna |
| netstandard2.0 | Microsoft.Data.SqlClient 6.1.7 | Ese paquete y System.Security.Cryptography.Pkcs 8.0.2, más sus transitivas |

No se instala el paquete NuGet System.Data.SqlClient para net472. El ensamblado
incluido en Framework, ese paquete y Microsoft.Data.SqlClient tienen distribución,
dependencias, características y mantenimiento distintos; no son intercambiables.
Las dependencias entre paquetes Arkyva conservan la misma versión preview.3.

## Medición reproducible antes/después

Antes de cambiar los proyectos se restauró preview.2 desde los nupkg locales
archivados en un consumidor net472 nuevo y caché vacía. Después se repitió con
preview.3. Ambos consumidores usan una sola PackageReference de producto Arkyva,
sin ProjectReference. Los números son los realmente resueltos, no mínimos inferidos
del nuspec. Windows tenía los targeting packs instalados; no se incluye como
dependencia de ejecución un paquete de ensamblados de referencia del SDK.

| Métrica net472 | preview.2 | preview.3 nativa |
| --- | ---: | ---: |
| Paquetes NuGet totales (incluidos cuatro Arkyva) | 45 | 4 |
| Paquetes externos a Arkyva | 41 | 0 |
| Dependencias transitivas, excluyendo referencia directa Arkyva | 44 | 3 |
| Dependencias SQL externas transitivas | 41 | 0 |
| Azure.* | 2 | 0 |
| Microsoft.Identity* (incluye IdentityModel) | 10 | 0 |
| Microsoft.Extensions.* | 6 | 0 |
| Binding redirects generados por el consumidor | 11 | 0 |
| Advertencias NuGet de restore | 0 | 0 |
| Actualizaciones de paquetes preexistentes en ese consumidor vacío | 0 | 0 |

Eliminadas del grafo nativo: Microsoft.Data.SqlClient/SNI, Azure.Core/Identity,
MSAL/Broker/NativeInterop e IdentityModel, Extensions Caching/Logging/Options/DI,
y las dependencias de buffers, memoria, JSON, criptografía y otras de ese grafo.
La lista completa con versiones está en los reportes JSON, no se elimina nada de
una aplicación existente automáticamente.

No se dispone del packages.config/csproj/Web.config del WCF real. Por ello el número
de actualizaciones o conflictos en **esa aplicación** no se conoce. La línea base
obliga a resolver versiones como System.Memory 4.6.3, Unsafe 6.1.2, System.Text.Json
8.0.6, MSAL 4.84.2 y Extensions 8.x; podrían entrar en conflicto con referencias
directas anteriores o cambios de identidad/carga de ensamblados. No son actualizaciones
observadas de un WCF que no fue proporcionado. Revisar su inventario y sus redirects
antes de retirar dependencias: otras bibliotecas pueden seguir necesitándolas.

El consumidor netstandard2.0 independiente resolvió 49 paquetes (45 externos,
incluida infraestructura implícita del target), seleccionó los cuatro activos
lib/netstandard2.0 y conservó Microsoft.Data.SqlClient 6.1.7. Se compiló como biblioteca;
no se interpreta su ausencia de exe.config como prueba de que un host no requiera
binding redirects. La reducción medida corresponde específicamente a net472.

Herramientas y evidencia local (artifacts está ignorado por Git):

- Test-Packages.ps1 inspecciona ambos grupos, rechaza dependencias nativas adicionales,
  comprueba la selección en project.assets.json y restaura ambos consumidores.
- Measure-PackageDependencies.ps1 lee assets y exe.config sin tocar recursos SQL.
- artifacts/dependency-reports/preview2-net472.json: línea base previa al cambio.
- artifacts/dependency-reports/1.0.0-preview.3-net472.json y 1.0.0-preview.3-netstandard20.json:
  paquetes, versiones, grupos, activos seleccionados, advertencias y redirects.

Para recrear la línea base, usar los cuatro paquetes preview.2 en un feed aislado,
crear consumidor net472 con una única PackageReference Arkyva preview.2 y caché nueva,
restaurar/compilar, y pasar su csproj a Measure-PackageDependencies.ps1 con
ReportName preview2-net472. El inspector actual exige multitargeting y no debe
usarse como si preview.2 ya incluyera esos activos. Para preview.3:

```powershell
./scripts/Validate-Packages.ps1 -Version 1.0.0-preview.3
```

## Evaluación de seguridad y mantenimiento

Microsoft recomienda Microsoft.Data.SqlClient para desarrollo nuevo y considera
heredada la familia System.Data.SqlClient. La elección nativa es una excepción acotada
para el consumidor Framework existente: las funciones que Arkyva utiliza están
disponibles y la reducción de dependencias es significativa. No equivale a recomendar
el proveedor antiguo para nuevas plataformas ni a garantizar soporte de nuevas
características SQL/Entra. [Referencia oficial](https://learn.microsoft.com/en-us/dotnet/api/system.data.sqlclient?view=netframework-4.7.2).

Los parches del proveedor incluido en Framework dependen del mantenimiento de Windows
y .NET Framework, no de actualizar un PackageReference System.Data.SqlClient. Mantener
un SO soportado, Framework actualizado y políticas TLS apropiadas. La auditoría NuGet
no audita los binarios del Framework/SO ni acredita su estado de parcheado. La
[advertencia CVE-2024-0056 de Microsoft](https://github.com/dotnet/announcements/issues/292)
explica por qué actualizar un paquete no basta para corregir el componente de Framework.
No se certificó aquí el estado de parches del servidor de producción.

### Cifrado y diferencias de configuración

Microsoft.Data.SqlClient 6.1.7 cifra por defecto; System.Data.SqlClient históricamente
no. Para **no introducir una degradación silenciosa**, el constructor net472 de
Arkyva agrega Encrypt=True cuando no se especifica. TrustServerCertificate sigue
en False por defecto. En netstandard2.0 no cambia la normalización del driver.
[Diferencias oficiales de migración](https://github.com/dotnet/SqlClient/blob/main/porting-cheat-sheet.md).

| Configuración | net472 | netstandard2.0 |
| --- | --- | --- |
| Encrypt omitido | Arkyva exige True | Default Mandatory del driver |
| Encrypt=True / Mandatory | Cifrado requerido | Cifrado requerido |
| Encrypt=False / Optional explícito | Conserva esa elección del consumidor | Conserva esa elección |
| TrustServerCertificate omitido | False | False |
| Encrypt=Strict | Rechazo claro; nunca convertir a True/False | Mantiene soporte del driver |
| HostNameInCertificate y otras keywords no soportadas | Rechazo del builder | Mantiene capacidades del driver |

No usar False/Optional ni TrustServerCertificate=True para reducir dependencias o
resolver errores de producción. Su aceptación explícita conserva la configuración
anterior, no los convierte en opciones recomendadas. En particular, no asumir que
el caso Encrypt=False con cifrado forzado por el servidor valida certificados igual
en ambos drivers. Producción debe usar **Encrypt=True;TrustServerCertificate=False**,
certificado SQL verificable, nombre correcto y cadena de confianza instalada.
TLS negociado depende del SO, políticas y servidor; Arkyva no modifica Schannel,
ServicePointManager ni protocolos. [Guía de cifrado](https://learn.microsoft.com/en-us/sql/connect/ado-net/encryption-and-certificate-validation).

Si una instalación exige Strict/TDS 8, pinning, HostNameInCertificate o autenticación
Entra moderna dependiente de Azure.Identity/MSAL, no actualizar a este activo net472
sin una estrategia compatible validada. No se eliminan keywords para conectar a toda
costa ni se promete emulación. Algunas capacidades antiguas de AD existen en Framework,
pero no se afirma equivalencia con la familia moderna. netstandard2.0 conserva el driver.

### Comparación de las operaciones utilizadas

| Área | Evaluación / validación |
| --- | --- |
| Conexiones | using por operación, OpenAsync y pooling del driver; no conexión compartida permanente |
| Comandos | Mismo SQL parametrizado, tamaños/tipos explícitos y mismas opciones SET |
| Transacciones | List conserva Serializable para count/página; BeginTransaction/Commit síncronos existentes, comandos/lecturas async; rollback de prueba verificado |
| Async | OpenAsync, ExecuteNonQueryAsync, ExecuteScalarAsync, ExecuteReaderAsync, ReadAsync y NextResultAsync conservados; no Result/Wait como reemplazo |
| Cancelación | La API actual no acepta CancellationToken; no se añadió ni se promete cancelación nueva |
| Guid/DateTime | Roundtrip de uniqueidentifier, datetime2(7) y restauración UTC de metadatos verificados |
| DateTimeOffset/varbinary | No son campos públicos nuevos; prueba SQL tipada de ida/vuelta en ambos drivers sin modificar el esquema |
| Concurrencia/recuperación | Mismas consultas compare-and-swap Revision y recibo SHA-256; pruebas reales en ambos targets |
| Excepciones | Repositorio directo lanza SqlException del driver del target; sus tipos no son intercambiables. El servicio conserva ArkyvaMetadataException/ArkyvaStorageException y causa interna |
| Autenticación integrada | Probada con identidad Windows contra LocalDB; permisos/identidad remota IIS y delegación requieren validación del despliegue |

## Validación y límites

Se compilaron ambos targets y se ejecutaron las seis suites existentes por separado:
activos nativos y activos Standard en runners Framework explícitos. Los tests de contrato
comprueban el TargetFrameworkAttribute cargado para no confundirlos. Se comparó la
superficie pública de los cuatro ensamblados entre targets y frente a preview.2 en
AppDomains aislados: sin diferencias. No se cambia el comportamiento de ArkyvaHost.

LocalDB temporal real: pruebas de inserción, filtros, paginación, errores, tipos,
concurrencia, recuperación y flujo físico; el consumidor NuGet nativo usa Current.
El contrato WCF del smoke consumer compila con System.ServiceModel del Framework,
sin agregarlo a la librería. No se alojó un endpoint WCF/IIS real.

Entorno: Windows, SDK/targeting pack net472 y runtime Framework 4.8.09032. No se
probó una máquina con runtime 4.7.2 exacto, SMB ni TLS remoto con certificado real.
LocalDB se usa con su configuración local de prueba explícita, no como evidencia
de TLS o autenticación remota. Retirar solo bases/carpetas/instancia creadas por los tests.
No se ejecutan migraciones sobre bases existentes ni se publican paquetes en esta fase.

Ejecución SQL: 252 comprobaciones nativas y 250 Standard; 18 internas en el consumidor
NuGet nativo y 28 controles de publicación, sin fallos. Los resúmenes SQL locales
están en artifacts/test-results/integration-summary.json y
standard-integration-summary.json; el flujo predeterminado vuelve a generar los
resúmenes offline e informa sus omisiones SQL. La instancia temporal ya se eliminó.
