# Reglas de desarrollo de Arkyva

- Consultar `docs/SPECIFICATION.md` antes de implementar funcionalidades.

- Desarrollar únicamente el alcance solicitado en cada tarea.

- Priorizar soluciones sencillas, mantenibles y compatibles con `netstandard2.0`.

- Mantener separados Core, proveedores de almacenamiento y persistencia de metadatos.

- No incorporar características futuras sin autorización.

- No modificar contratos públicos existentes sin justificar el cambio.

- Agregar pruebas para cada nueva funcionalidad.

- Ejecutar compilación y pruebas antes de finalizar, cuando el entorno lo permita.

- No afirmar que una prueba pasó si no fue ejecutada.

- Documentar decisiones arquitectónicas relevantes.

- Si una tarea requiere cambios fuera de su alcance, explicar la dependencia antes de implementarla.
