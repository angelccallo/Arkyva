# Cambios

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
