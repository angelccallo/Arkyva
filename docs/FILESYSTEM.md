# Proveedor FileSystem (Fase 2)

`Arkyva.Storage.FileSystem` implementa `IStorageProvider` con `System.IO`, sin
paquetes adicionales, SQL Server ni reglas documentales. La biblioteca sigue en
`netstandard2.0`; las pruebas locales se ejecutan como `net472` en Windows.

## Inicialización y uso

```csharp
using System.IO;
using Arkyva.Core.Interfaces;
using Arkyva.Storage.FileSystem;

IStorageProvider storage = new FileSystemStorageProvider(@"D:\Arkyva\Files");
// Alternativa Windows, con la identidad y permisos de la aplicación:
// IStorageProvider storage = new FileSystemStorageProvider(@"\\FILE-SERVER\Documentos");

string key = "2026/10/a83f9c2d.bin";
using (Stream source = File.OpenRead(@"D:\Entrada\documento.pdf"))
    await storage.SaveAsync(key, source);

bool exists = await storage.ExistsAsync(key);
using (Stream content = await storage.ReadAsync(key))
using (Stream target = File.Create(@"D:\Salida\documento.pdf"))
    await content.CopyToAsync(target);

await storage.DeleteAsync(key); // Borrado físico; ausente también es válido.
```

El constructor exige una ruta absoluta local o UNC y crea el directorio base si
no existe. No modifica credenciales ni permisos. Las rutas UNC requieren Windows
y acceso SMB desde la cuenta que ejecuta la aplicación. En otros sistemas un
recurso remoto debe estar montado y configurarse mediante su ruta local absoluta.
No hay comprobación anticipada de todos los permisos: los errores se propagan al
realizar la operación correspondiente.

## Claves y seguridad

Una storageKey es un identificador relativo interno generado por el futuro servicio
Arkyva; no es un nombre original ni una ruta pública. El proveedor no genera GUID
para claves finales. Acepta segmentos con letras ASCII, dígitos, punto, guion y
guion bajo, separados por `/` o `\`. Rechaza segmentos vacíos, `.` y `..`, puntos
finales, nombres de dispositivo Windows, espacios, caracteres de control, dos
puntos (incluidos ADS), rutas absolutas y otros caracteres fuera de ese alfabeto.
No decodifica URL. El prefijo `.arkyva-` queda reservado en cada segmento para los
temporales y no puede utilizarse como clave, incluso para leerlos.

La ruta normalizada debe permanecer bajo la base, con su separador de límite.
Se rechazan enlaces simbólicos y puntos de reanálisis detectados en el destino,
los directorios y los antecesores de la base. La raíz y su árbol deben pertenecer
a una ubicación confiable: otros usuarios no deben poder sustituir directorios,
crear enlaces o manipular archivos internos. Las comprobaciones portables de
System.IO no eliminan carreras entre comprobación y uso frente a un atacante con
permisos para cambiar el árbol. Tampoco detectan todos los alias o enlaces duros.
No se promete aislamiento frente a administradores del sistema de archivos.

## Streams, errores y concurrencia

Save valida el stream, lee desde su posición actual y lo deja abierto, incluso si
falla. No necesita Length ni Seek. CopyToAsync y FileStream asíncrono utilizan
buffers acotados de 81920 bytes; no se carga el archivo completo en memoria.
Read devuelve un FileStream desde posición cero: el consumidor debe liberarlo.
La lectura comparte acceso de lectura; en Windows un lector abierto puede impedir
el borrado, que en ese caso propaga el error de compartición.

Save crea directorios y un archivo exclusivo `.arkyva-<guid>.tmp` junto al destino.
Tras copiar, vaciar buffers y cerrar el stream, File.Move publica el nombre final
sin reemplazarlo. No hay bloqueos estáticos: el sistema de archivos arbitra la
publicación incluso entre procesos. Para dos escrituras de la misma clave sin
borrados concurrentes, una gana y la otra falla; sus temporales son independientes.
Un Delete concurrente puede borrar un resultado ya publicado; el proveedor no
impone transacciones ni coordinación documental.

Si la copia o la publicación fallan, se intenta eliminar únicamente el temporal
creado por esa operación. Un fallo de limpieza de E/S o permisos no oculta el error
original. No se eliminan directorios vacíos. Un cierre abrupto, un corte de red o
la pérdida de permisos pueden dejar temporales; esta fase no incluye un limpiador.

Excepciones relevantes (sin envolver los errores originales de System.IO):

- ArgumentException: configuración, clave o stream no legible inválidos.
- ArgumentNullException: stream nulo.
- FileNotFoundException: Read sobre archivo o directorio padre ausente.
- IOException y sus subtipos: conflicto de destino, enlaces rechazados, errores
  de red, compartición o escritura; también pueden provenir del stream de entrada.
- UnauthorizedAccessException y, según el entorno, SecurityException: permisos.
- PathTooLongException, NotSupportedException u otros errores de la plataforma
  cuando la ruta o el recurso no son admitidos.

Exists devuelve false para archivos ausentes o rutas que son directorios. Usa
GetAttributes, evitando que File.Exists convierta errores de acceso en false.
Delete es físico e idempotente para archivos o directorios padres ausentes; no
borra directorios ni administra estados documentales. Los métodos sin E/S asíncrona
nativa devuelven tareas completadas y pueden lanzar errores antes de devolverlas;
no se usan tareas de fondo para simular asincronía.

## Atomicidad local y SMB

La publicación mediante rename en el mismo directorio evita exponer un nombre
final parcialmente escrito en sistemas de archivos que soportan esa operación
atómica. La garantía efectiva depende del sistema y del servidor SMB. FlushAsync
no promete persistencia física ante cortes de energía. Un fallo de red puede dejar
un resultado incierto (el servidor pudo completar la publicación antes del error);
no se reintenta automáticamente ni se garantiza ejecución exactamente una vez.
No se promete atomicidad distribuida ni durabilidad ante fallos de servidor.

La cuenta de ejecución necesita acceso a todos los antecesores, crear directorios,
crear/escribir/leer archivos, renombrar y borrar temporales y archivos definitivos.
En SMB deben concederse tanto permisos del recurso compartido como del sistema de
archivos. No se requieren permisos administrativos.

## Pruebas

```powershell
dotnet build Arkyva.sln --configuration Release
& ./tests/Arkyva.ContractTests/bin/Release/net472/Arkyva.ContractTests.exe
& ./tests/Arkyva.FileSystemTests/bin/Release/net472/Arkyva.FileSystemTests.exe
```

Se mantiene el ejecutable de pruebas sin framework externo de la Fase 1. Devuelve
código 1 si falla; no se descubre mediante dotnet test. Utiliza una carpeta temporal
única, verifica su ruta absoluta antes de borrarla y limpia en finally.

Cubre integridad, streams no posicionables de más de 32 MiB, posición de entrada,
propiedad de streams, claves peligrosas en las cuatro operaciones, configuración,
archivos ausentes, borrado idempotente, conflictos, errores de lectura y escritura,
limpieza tras fallos de copia y publicación, visibilidad durante escritura,
escrituras concurrentes en claves distintas y colisión entre dos instancias sobre
la misma clave, incluida una colisión entre procesos separados. Las barreras controladas fuerzan escrituras simultáneas sin basarse
en demoras arbitrarias. No se exige ni se ha probado un servidor SMB real. Las
pruebas no simulan pérdida de energía, cambios hostiles de directorios ni ACL remotas.

Resultado verificado de esta fase: compilación Release de los cinco proyectos con
cero errores y cero advertencias; 21 comprobaciones FileSystem y 9 de contratos
satisfactorias ejecutadas sobre net472 en Windows. No se modificaron los contratos
de Core, SPECIFICATION.md ni el proyecto SQL Server.

## Inventario de Fase 5

FileSystemStorageProvider implementa la capacidad opcional IStorageDiagnostics.
InspectAsync realiza un inventario acotado de solo lectura, identifica el patrón
exacto de temporales y omite enlaces. No determina abandono ni borra archivos.
StorageRecoveryService convierte sus claves técnicas en referencias opacas para
el diagnóstico; no publicar StorageInventory directamente al consumidor.
Consultar [RECOVERY.md](RECOVERY.md) para conservación de temporales antiguos,
operaciones activas y contenido definitivo sin referencia confirmada.
