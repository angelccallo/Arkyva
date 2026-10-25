# Cambios

## 1.0.0-preview.4 — en preparación, no publicada por esta tarea

- Upload, Download, GetInfo, List, Exists y Delete síncronos nativos, complementarios
  a Async. E/S directa de FileSystem y ADO.NET; sin bloqueo de tareas ni Task.Run.
- Capacidades ISynchronousArkyvaService, ISynchronousStorageProvider,
  ISynchronousMetadataRepository e ISynchronousRecoveryMetadataRepository opcionales.
  Interfaces Async existentes conservadas para no romper implementadores; extensiones
  en namespace Arkyva permiten Current.Upload con el tipo anterior de Current.
- Reglas/validaciones, snapshots, comandos SQL parametrizados y predicados CAS
  compartidos. Recibo SHA-256 y recuperación interoperables entre ambas modalidades.
- Pruebas nativas, fallos parciales, concurrencia mixta y consumidor NuGet independiente.
- Se mantienen ambos targets, drivers/cifrado y dependencias de preview.3, host,
  esquema SQL, licencia y estructura física. Publicación exclusivamente manual.

## 1.0.0-preview.3 — en preparación, no publicada por esta tarea

- Los cuatro paquetes incluyen net472 y netstandard2.0 con la misma API pública.
- net472 utiliza System.Data.SqlClient incluido en System.Data del Framework,
  sin paquete SQL adicional; netstandard2.0 conserva Microsoft.Data.SqlClient 6.1.7
  y Pkcs 8.0.2. En un consumidor limpio net472: 45 → 4 paquetes, 11 → 0 redirects.
- Encrypt omitido conserva el default cifrado anterior en net472; Mandatory/Optional
  conservan su significado explícito. Strict y keywords no soportadas se rechazan
  sin degradación. Revisar DEPENDENCIES.md antes de actualizar: el proveedor nativo
  es heredado y depende de parches Windows/Framework; no equivale al driver moderno.
- No cambian ArkyvaHost, builder, contratos, consultas SQL, esquema ni claves físicas.
- Validación de ambos targets, consumidores NuGet independientes, contrato WCF de
  compilación y comparación de superficie pública frente a preview.2.
- Publicación manual mediante el workflow existente, sin login/push automático.

## 1.0.0-preview.2 — en preparación, no publicada por esta tarea

- ArkyvaHost.Configure(Action<ArkyvaHostOptions>) y Current: acceso global opcional
  a un IArkyvaService compartido por AppDomain, seguro frente a concurrencia.
- Configuración única, publicación solo tras Build exitoso y reintento ante fallos;
  rechazo de configuración duplicada/recursiva y options fuera del callback.
- ArkyvaBuilder inmutable y contratos existentes conservados. Sin nuevas dependencias,
  cambios de proveedores, esquema SQL ni operaciones documentales.
- Ejemplos Application_Start/WCF, pruebas aisladas por AppDomain y consumidor NuGet net472.
- Cuatro paquetes con versión coherente 1.0.0-preview.2; publicación manual por el
  workflow existente, nunca automática. No hay migraciones SQL nuevas.
