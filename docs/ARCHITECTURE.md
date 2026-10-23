# Arquitectura de Arkyva - Fase 1

## Alcance y dependencias

Las tres bibliotecas usan `netstandard2.0`. Core define los contratos y modelos,
sin referencias a los proveedores. FileSystem y SqlServer referencian unicamente
Core, nunca entre si. En esta fase sus ensamblados son intencionalmente vacios:
no hay implementaciones que simulen exito ni metodos que lancen NotImplementedException.

```text
Arkyva.Storage.FileSystem --> Arkyva.Core <-- Arkyva.Metadata.SqlServer
                                   ^
                      Arkyva.ContractTests (net472)
```

- `src/Arkyva.Core/Interfaces`: API del consumidor, almacenamiento y metadatos.
- `src/Arkyva.Core/Models`: informacion publica, registro tecnico, estado y paginacion.
- `src/Arkyva.Core/Services` y `Exceptions`: espacios reservados sin implementaciones.
- `src/Arkyva.Storage.FileSystem`: proyecto reservado para el proveedor fisico.
- `src/Arkyva.Metadata.SqlServer/Repositories` y `Scripts`: espacios reservados;
  el esquema `arkyva` y los scripts versionados se incorporaran al implementar persistencia.
- `tests/Arkyva.ContractTests`: comprobaciones ejecutables de contratos y consumo desde net472.

## Contratos y decisiones

`IArkyvaService` conserva las seis operaciones solicitadas. Recibe streams y nombres
originales y devuelve GUID, streams o metadatos publicos. `FileInfo` no expone claves,
rutas ni identificadores del proveedor. `FileMetadata` extiende esos datos para el
contrato de persistencia; no es el tipo devuelto por la API del consumidor.
La herencia permite reutilizar los campos sin duplicarlos; no hay mapeo implementado.

`IStorageProvider` conserva exactamente las cuatro firmas minimas de la especificacion.
Solo administra contenido fisico. Save no sobrescribe y unicamente publica contenido
completo. Delete fisico es idempotente. El llamador conserva la propiedad del stream
recibido y debe liberar los streams devueltos. Los errores del proveedor se propagan;
una lectura inexistente produce FileNotFoundException.

`IMetadataRepository` define insercion, consulta individual, listado, existencia y
actualizacion. Get permite consultar cualquier estado para la futura coordinacion;
List y Exists consideran solo registros activos. Update no inserta registros ausentes.
La eliminacion logica se representara actualizando Status y DeletedUtc. No se define
una transaccion distribuida ni se garantiza atomicidad entre contenido y metadatos.
La coordinacion, concurrencia y recuperacion se resolveran al implementar los servicios.

Decisiones sencillas donde no habia una firma prescrita:

- GetInfo devuelve null para archivos ausentes o no activos; Download lanza
  FileNotFoundException. Delete logico es idempotente y no implica borrado fisico.
- Paginas basadas en uno, tamano inicial 50; numeros o tamanos no positivos deberan
  rechazarse. Filtros opcionales de nombre original y extension exactos, combinados
  con AND y comparacion ordinal sensible a mayusculas. Orden estable ascendente
  por CreatedUtc y FileId. TotalCount cuenta todos los resultados antes de paginar.
- Pending es el estado inicial, seguido de Active, Deleted o Failed segun la operacion.
  Esto solo define vocabulario: no implementa transiciones ni recuperacion.
- Modelos simples mutables. Las validaciones pertenecen a las futuras implementaciones;
  las propiedades no realizan E/S ni validacion automatica en esta fase.
- Sin paquetes explicitos en bibliotecas. El SDK restaura el paquete implicito de
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
Las futuras pruebas unitarias de servicios, integracion FileSystem/SQL Server,
concurrencia y recuperacion se anadiran cuando existan esas implementaciones;
no se conectan bases de datos ni se realizan operaciones de archivos en estas pruebas.
La aplicacion de ejemplo, API fluida, scripts SQL y paquetes NuGet quedan para fases
posteriores. SPECIFICATION.md permanece sin cambios.

## Resultado de verificacion de la Fase 1

- Compilacion Release de los cuatro proyectos: cero errores y cero advertencias.
- Ejecutable de contratos net472: nueve comprobaciones satisfactorias.
- Sin operaciones de archivos, conexiones SQL ni pruebas de integracion simuladas.
- La restauracion inicial requirio acceso a NuGet fuera del entorno restringido.
