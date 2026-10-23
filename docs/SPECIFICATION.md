# PROMPT: Desarrollo de Arkyva v1.0

## CONTEXTO

Se requiere desarrollar **Arkyva**, una librería NuGet de almacenamiento de archivos para C#, inspirada en la facilidad de integración de Hangfire y en los conceptos básicos de repositorio documental de Alfresco.

Arkyva permitirá a cualquier aplicación guardar, recuperar, consultar y eliminar archivos sin conocer su ubicación física ni administrar directamente sus metadatos.

La solución será **embebida, ligera, independiente del negocio y extensible**, sin requerir la instalación de servidores, servicios Windows ni procesos adicionales.

**Tecnologías iniciales:**

- **.NET Standard 2.0** para las librerías, garantizando compatibilidad con .NET Framework 4.7.2 y versiones modernas de .NET.

- **.NET Framework 4.7.2** para la aplicación de ejemplo y las pruebas de integración.

- **SQL Server** para almacenar y administrar metadatos.

- **System.IO** para el proveedor inicial de almacenamiento FileSystem.

- **NuGet** como mecanismo de distribución.

Cada instalación de Arkyva administrará su propio repositorio lógico, compuesto por una base de datos de metadatos y una ubicación física de almacenamiento configurable.

Aunque inicialmente se utilizará en una aplicación .NET Framework 4.7.2, la arquitectura deberá permitir su reutilización en otros proyectos sin modificar su implementación.

A futuro, Arkyva podrá incorporar nuevos proveedores de almacenamiento y un dashboard web administrativo similar al de Hangfire.

## OBJETIVOS

### 1. Diseñar una arquitectura modular

Crear una solución organizada inicialmente en los siguientes proyectos:

```
Arkyva.sln│├── Arkyva.Core│   ├── Interfaces│   ├── Models│   ├── Services│   └── Exceptions│├── Arkyva.Storage.FileSystem│   └── FileSystemStorageProvider│├── Arkyva.Metadata.SqlServer│   ├── Repositories│   └── Scripts│└── Arkyva.Samples.NetFramework
```

Los tres primeros proyectos deberán utilizar `netstandard2.0`. El proyecto de ejemplo utilizará `net472`.

Mantener responsabilidades separadas y dependencias mínimas.

### 2. Implementar una API pública sencilla

Definir un servicio principal que permita realizar las operaciones básicas:

- `UploadAsync`: guardar un archivo mediante `Stream`.

- `DownloadAsync`: recuperar el contenido mediante `Guid`.

- `GetInfoAsync`: consultar metadatos.

- `ListAsync`: listar archivos con paginación y filtros básicos.

- `ExistsAsync`: verificar la existencia de un archivo.

- `DeleteAsync`: realizar una eliminación lógica.

Ejemplo de consumo esperado:

```
Guid id = await arkyva.UploadAsync(    stream,    "documento.pdf");var archivo = await arkyva.DownloadAsync(id);var info = await arkyva.GetInfoAsync(id);await arkyva.DeleteAsync(id);
```

La librería debe administrar internamente las rutas físicas y los nombres de almacenamiento.

Los streams devueltos deberán permitir la liberación correcta de recursos mediante `Dispose`.

### 3. Implementar proveedores de almacenamiento

Definir una interfaz `IStorageProvider` independiente de la tecnología utilizada.

Como mínimo, debe permitir:

```
public interface IStorageProvider{    Task SaveAsync(        string key,        Stream content);    Task<Stream> ReadAsync(string key);    Task<bool> ExistsAsync(string key);    Task DeleteAsync(string key);}
```

Implementar inicialmente únicamente **FileSystem**, utilizando `System.IO`.

Debe permitir almacenar archivos en:

- Directorios locales.

- Carpetas compartidas de Windows mediante rutas UNC (SMB).

Diseñar la interfaz para permitir incorporar posteriormente proveedores como:

- SFTP.

- Amazon S3.

- Azure Blob Storage.

- SharePoint.

- Otros proveedores compatibles.

El proveedor será responsable exclusivamente de las operaciones físicas de almacenamiento, sin conocer SQL Server ni las reglas del repositorio documental.

### 4. Implementar almacenamiento de metadatos

Crear un esquema independiente llamado `arkyva` en SQL Server.

La librería deberá administrar sus propias tablas, sin depender de las tablas de negocio.

Registrar como mínimo:

- `FileId` (GUID).

- Nombre original del archivo.

- Extensión.

- Tipo MIME.

- Tamaño en bytes.

- Identificador del proveedor.

- Clave interna de almacenamiento (`StorageKey`).

- Fecha de creación en UTC.

- Fecha de eliminación, cuando corresponda.

- Estado del archivo.

Separar la persistencia mediante una interfaz `IMetadataRepository`.

Incluir scripts SQL versionados de creación y actualización del esquema.

### 5. Implementar configuración sencilla

Diseñar una API de configuración fluida, inspirada en Hangfire:

```
Arkyva.Configure(options =>{    options.UseSqlServer(connectionString);    options.UseFileSystem(        @"\\FILE-SERVER\Documentos"    );});
```

El ejemplo representa la API pública que se debe desarrollar.

La configuración debe permitir seleccionar el proveedor de almacenamiento y la conexión SQL Server.

Cada instalación tendrá su propio repositorio lógico. No se requiere `ApplicationId`, registro central de aplicaciones ni administración multiaplicación.

La librería debe integrarse con aplicaciones existentes sin imponer un contenedor de inyección de dependencias específico.

### 6. Implementar almacenamiento seguro

Considerar desde la primera versión:

- Generación de identificadores únicos mediante GUID.

- Nombres físicos independientes del nombre original.

- Lectura y escritura mediante streams, evitando cargar archivos completos en memoria.

- Organización interna de archivos mediante claves generadas por Arkyva.

- Validación configurable de tamaño máximo y tipos de archivo permitidos.

- Manejo de excepciones y errores de almacenamiento.

- Operaciones seguras ante solicitudes concurrentes.

- Escrituras temporales y publicación final del archivo para evitar exponer contenido incompleto.

- Estados de operación para detectar registros o archivos incompletos.

- Mecanismos básicos de recuperación y limpieza ante fallas entre SQL Server y el sistema de archivos.

No asumir transacciones atómicas entre SQL Server y FileSystem.

### 7. Mantener independencia del negocio

Arkyva debe trabajar exclusivamente con archivos y metadatos técnicos.

No debe conocer entidades como contratos, clientes, facturas, empleados u otros conceptos de negocio.

La aplicación consumidora decidirá cómo relacionar el `FileId` devuelto con sus propias entidades.

Ejemplo:

```
Guid fileId = await arkyva.UploadAsync(    stream,    "contrato.pdf");// La aplicación decide dónde utilizar fileId.
```

Arkyva no debe imponer relaciones con tablas externas.

### 8. Preparar extensibilidad y futuro dashboard

Diseñar los contratos y servicios para incorporar posteriormente un paquete opcional:

`Arkyva.Dashboard`

Este permitirá consultar:

- Archivos almacenados.

- Metadatos.

- Tamaño total utilizado.

- Estado del repositorio.

- Proveedor configurado.

- Archivos eliminados o con errores.

- Descarga administrativa de archivos autorizados.

El dashboard deberá poder integrarse en una aplicación web existente, sin exigir un servidor adicional.

**No implementar el dashboard en la versión 1.0.** Únicamente evitar decisiones arquitectónicas que dificulten su incorporación futura.

### 9. Pruebas y documentación

Incluir:

- Pruebas unitarias de los servicios principales.

- Pruebas de integración con FileSystem y SQL Server.

- Pruebas de concurrencia básica.

- Pruebas de recuperación ante fallas.

- Aplicación de ejemplo en .NET Framework 4.7.2.

- Documentación de instalación, configuración y consumo.

- Preparación de los proyectos para distribuirse como paquetes NuGet.

## RESTRICCIONES

1. No desarrollar un gestor documental completo como Alfresco.

2. No implementar soporte multiaplicación centralizado, `ApplicationId` ni aislamiento lógico por sistema.

3. No requerir servicios Windows, servidores de aplicaciones adicionales, brokers o procesos residentes.

4. No almacenar archivos binarios dentro de SQL Server.

5. No acoplar Arkyva a entidades, tablas o reglas de negocio específicas.

6. No exponer rutas físicas ni detalles del proveedor en la API pública.

7. No utilizar los nombres originales como identificadores físicos.

8. No utilizar rutas absolutas como identificadores públicos.

9. No implementar versionamiento documental en esta primera versión.

10. No implementar OCR, workflows, búsqueda de texto completo ni funcionalidades documentales avanzadas.

11. No implementar inicialmente proveedores SFTP, S3, Azure Blob ni SharePoint; únicamente preparar la arquitectura.

12. No desarrollar todavía el dashboard web.

13. No incorporar Hangfire, RabbitMQ ni procesos en segundo plano como dependencias obligatorias.

14. No introducir microservicios, arquitecturas distribuidas ni patrones innecesarios.

15. No utilizar APIs o dependencias incompatibles con .NET Standard 2.0 y .NET Framework 4.7.2.

16. No realizar migraciones automáticas destructivas de base de datos.

17. No permitir sobrescrituras accidentales ni archivos parcialmente escritos visibles como activos.

18. No implementar características futuras que no sean necesarias para el MVP.

## PRINCIPIOS DE DISEÑO

- **Simplicidad:** instalar, configurar y utilizar Arkyva debe ser tan sencillo como integrar una librería NuGet.

- **Abstracción:** el consumidor interactúa con archivos mediante identificadores únicos, nunca mediante rutas físicas.

- **Separación de responsabilidades:** Arkyva.Core administra el repositorio; Storage Providers administran el contenido físico; Metadata Providers administran los metadatos.

- **Extensibilidad:** permitir nuevos proveedores sin modificar los servicios públicos.

- **Independencia:** la librería no debe depender del dominio de negocio ni de un framework web.

- **Compatibilidad:** priorizar .NET Standard 2.0 para asegurar su uso desde .NET Framework 4.7.2 y .NET moderno.

- **Confiabilidad:** administrar correctamente concurrencia, errores, recuperación e integridad básica.

- **Mantenibilidad:** priorizar código claro, modular y fácil de probar sobre abstracciones excesivas.

## RESULTADO ESPERADO

Entregar **Arkyva v1.0**, una librería NuGet funcional, ligera y extensible, capaz de administrar un repositorio básico de archivos con metadatos persistidos automáticamente en SQL Server y contenido almacenado mediante un proveedor FileSystem configurable.

El usuario de Arkyva deberá poder instalar la librería, configurar SQL Server y una ruta de almacenamiento, y comenzar a guardar y recuperar archivos mediante una API sencilla.

La primera versión deberá resolver correctamente estas necesidades sin incorporar complejidad innecesaria, dejando una base sólida para futuros proveedores y un dashboard administrativo.

**Prioridad absoluta: construir un MVP funcional, confiable y sencillo antes de agregar características avanzadas.**
