# Seguridad y límites del MVP

La autenticación y autorización de usuarios finales son responsabilidad de la
aplicación consumidora, tanto para consultas como para descargas, borrados y
mantenimiento. Un GUID no es una credencial ni un permiso. Arkyva no implementa
usuarios, roles, aislamiento por aplicación ni validación de reglas de negocio.

## Controles revisados

- Claves relativas con alfabeto restringido; rechazo de traversal, rutas absolutas,
  ADS, dispositivos y nombres reservados. Se comprueba contención bajo la base.
- Detección de enlaces/reparse points, sin prometer protección frente a carreras
  de un actor que pueda sustituir directorios o modificar contenido externamente.
- Temporales exclusivos y rename sin sobrescritura. No se leen mediante claves
  públicas y no se declaran abandonados solo por edad o desbloqueo.
- SQL parametrizado y tipos/tamaños explícitos; datos de filtro no son SQL ejecutable.
- Revision condicional para estados; recibos inmutables y verificación streaming
  antes de recuperación. No hay transacción distribuida ni borrado por incertidumbre.
- DTOs públicos copiados y stream de lectura envuelto sin FileStream.Name.
- Excepciones con mensajes principales seguros; InnerException/ToString se reservan
  para diagnósticos protegidos y pueden contener rutas o información de conexión.
- Ejecución normal sin permisos administrativos. Solo instalación del esquema y
  fixture SQL tienen permisos DDL; el fixture crea recursos nuevos aislados.

El árbol físico y la cuenta SQL deben pertenecer a una configuración confiable.
Proteger ACL del directorio, permisos de recurso SMB y credenciales en la aplicación.
No ejecutar mantenimiento con conexiones o directorios de producción durante pruebas.

## Dependencias

La auditoría inicial señaló System.Security.Cryptography.Pkcs 8.0.1 por
[GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx) y
[GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7), ambos
corregidos en 8.0.2. Se fijó esa versión compatible en el proveedor SQL y la auditoría
posterior no encontró avisos conocidos en las dependencias resueltas. No se afirma
haber demostrado explotación de esas rutas CMS mediante la API de Arkyva.

Ejecutar scripts/Audit.ps1 antes de cada entrega. Un resultado limpio corresponde
a las versiones y al feed consultados en esa fecha; no certifica ausencia de fallos.
No se suprimen avisos ni se interpreta un error de red como auditoría satisfactoria.

## Límites explícitos

La política configurable de tamaño máximo y tipos permitidos prevista en
SPECIFICATION.md no está implementada en esta preview. El host debe restringir
cargas antes de exponer el servicio a entradas no confiables; no se anuncia como
requisito de v1 estable ya cumplido. Tampoco se detecta MIME: se usa octet-stream.

SHA-256 aquí es evidencia de contenido para recuperación, no firma, autorización,
protección contra administradores ni auditoría de todas las descargas Active. Los
históricos sin recibo quedan para diagnóstico conservador. SMB puede confirmar
operaciones tarde o perder durabilidad; se necesitan pruebas del entorno concreto.
El borrado lógico no revoca streams ya abiertos. Los DTOs técnicos de proveedores
(FileMetadata, StorageInventory) no deben publicarse directamente como DTOs del host.
