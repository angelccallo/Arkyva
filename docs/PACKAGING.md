> Este documento describe validación local previa. La distribución oficial será
> NuGet.org mediante el [procedimiento OIDC](NUGET_PUBLISHING.md); todavía no se publicó.

# Empaquetado y validación local

## Paquetes y procedencia de metadatos

Todos usan 1.0.0-preview.1 y netstandard2.0. src/Directory.Build.props centraliza
versión, autor, licencia y repositorio solo para bibliotecas. Authors es Angel Ccallo
(obtenido de git log), copyright angelccallo de LICENSE y RepositoryUrl procede de
origin: https://github.com/angelccallo/Arkyva. No se modificó la licencia MIT.

| PackageId | Dependencias principales |
| --- | --- |
| Arkyva | Core, Storage.FileSystem, Metadata.SqlServer de la misma preview |
| Arkyva.Core | Contratos y servicios independientes de proveedores |
| Arkyva.Storage.FileSystem | Arkyva.Core |
| Arkyva.Metadata.SqlServer | Arkyva.Core, Microsoft.Data.SqlClient 6.1.7 y System.Security.Cryptography.Pkcs 8.0.2 |

Pkcs ya era transitiva del driver: se añadió una restricción directa a la versión
corregida por los avisos de octubre de 2026, no una función criptográfica nueva.
Las dependencias transitivas completas aparecen en project.assets.json y se auditan.
Cada nupkg contiene DLL/XML de su ensamblado, README y LICENSE exacta. SqlServer
incluye V001–V004 en contentFiles/any/any/Scripts, sin instalación automática.
No se generan símbolos, paquetes de pruebas ni del ejemplo. No hay nueva fachada:
el proyecto de composición de la Fase 6 ya cumple esa función.

## Generación e inspección

Desde la raíz, en Windows con los requisitos net472:

```powershell
dotnet restore Arkyva.sln
dotnet build Arkyva.sln -c Release --no-restore -warnaserror
./scripts/Audit.ps1
./scripts/Test.ps1
dotnet pack Arkyva.sln -c Release --no-build --no-restore -o artifacts/packages
./scripts/Test-Packages.ps1
```

La auditoría debe poder consultar el feed: un fallo de consulta no se interpreta
como ausencia de vulnerabilidades. Test-Packages comprueba cuatro manifiestos,
versión/dependencias, licencia, documentación y contenido permitido del ZIP. Genera
artifacts/package-inspection.json con SHA-256 de los paquetes, sin publicarlos.

El consumidor se copia a artifacts/package-smoke/<guid>/consumer y utiliza SOLO
PackageReference Arkyva. Su caché nueva está en un directorio hermano packages,
fuera del árbol de fuentes del consumidor para evitar que MSBuild examine assets
de otros frameworks. Se verificó que incluir esa caché dentro del proyecto genera
conflictos MSB3277; se corrigió el fixture en vez de silenciar advertencias.

NuGet.Config generado habilita feed local y NuGet.org con source mapping: Arkyva y
Arkyva.* se resuelven exclusivamente del feed local; las demás dependencias desde
NuGet.org. No se reutiliza una caché antigua que pueda ocultar paquetes incorrectos.
Se comprueba en assets que las cuatro bibliotecas son paquetes, nunca proyectos.
El consumidor compila con warnings como errores, genera binding redirects y ejecuta
la API pública net472. latest.txt identifica su ejecutable para la integración SQL.
La plantilla tests/Arkyva.PackageSmoke no se agrega a la solución para evitar una
restauración circular antes de generar el feed; no es un proyecto distribuible.

## Instalar en otra aplicación

Crear un NuGet.Config del consumidor con la ruta absoluta a artifacts/packages:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="arkyva-local" value="C:\ruta\Arkyva\artifacts\packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="arkyva-local">
      <package pattern="Arkyva" />
      <package pattern="Arkyva.*" />
    </packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

Después instalar únicamente Arkyva:

```powershell
Install-Package Arkyva -Version 1.0.0-preview.1
# O desde una consola, en un proyecto net472:
dotnet add package Arkyva --version 1.0.0-preview.1
```

Conservar NuGet.org para resolver dependencias externas y un cliente NuGet compatible
con source mapping. No se afirma que los IDs estén reservados ni que esta preview
esté publicada en NuGet.org. Verificar titularidad/disponibilidad antes de publicar.
Las aplicaciones net472 necesitan binding redirects adecuados en .exe.config o
Web.config, además de todas las DLL y componentes nativos del driver. La prueba
incluida habilita los redirects de escritorio; otros hosts necesitan su validación.

## Integración real del paquete

Configurar ARKYVA_SQL_TEST_CONNECTION hacia SQL de pruebas. El runner crea su propia
base ArkyvaTests_<guid> y nunca utiliza Initial Catalog existente. Para incluir el
consumidor instalado:

```powershell
$env:ARKYVA_PACKAGE_CONSUMER_EXE = (Get-Content artifacts/package-smoke/latest.txt -Raw).Trim()
./scripts/Test.ps1 -RunSqlIntegration
```

El runner aplica V001–V004, prueba repositorio/servicio/recuperación reales y pasa
solo su conexión aislada al proceso consumidor. Este último utiliza el builder y
las seis operaciones, con carpeta temporal única, sin referencias concretas a SQL
ni proveedores. Comprueba contenido idéntico y borrado lógico sin borrar definitivos.
Su teardown físico solo elimina el directorio de la prueba; el runner elimina su
base nueva. No se comparte ninguna carpeta de producción.

## GitHub Actions

.github/workflows/ci.yml usa windows-2022 y SDK 9. Restaura, compila Release con
warnings como errores, audita dependencias, ejecuta las seis suites, empaqueta,
inspecciona y ejecuta el consumidor limpio. Solo sube artefactos de validación.
No contiene nuget push, publicación, tags, releases ni permisos de escritura al código.

SQL externo está en un job separado, condicionado a workflow_dispatch con
run_sql_integration=true y environment sql-integration. Configurar protección de ese
entorno y su secreto ARKYVA_SQL_TEST_CONNECTION para un servidor de pruebas accesible.
Un secreto faltante produce fallo explícito. No se entrega a pull requests de forks.
No se ha ejecutado el workflow remoto porque esta tarea no autoriza push; se
validaron localmente los comandos y sintaxis PowerShell y se revisó el YAML.

## Evidencia y publicación

Los artefactos generados permanecen ignorados por Git. Guardar los nupkg e informes
como artefactos de validación, no en el historial fuente. Consultar RELEASE_READINESS.md
antes de una publicación manual autorizada. No se publica ni se crea un release/tag
en esta fase; no existe un API key de NuGet en archivos o workflows.
