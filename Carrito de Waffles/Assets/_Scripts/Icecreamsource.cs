// ═══════════════════════════════════════════════════════════════════
// ICE CREAM SOURCE — OBSOLETO
//
// Este archivo ya no contiene lógica activa.
// Fue reemplazado por tres instancias de ItemSource.cs (una por sabor).
//
// MIGRACIÓN:
//   1. Eliminar el GameObject "IceCreamSource" de la escena.
//   2. Crear tres GameObjects separados:
//        ItemSource_Vanilla     → ItemSource (producedItemType = IceCreamVanilla)
//        ItemSource_Strawberry  → ItemSource (producedItemType = IceCreamStrawberry)
//        ItemSource_Chocolate   → ItemSource (producedItemType = IceCreamChocolate)
//   3. Asignar a cada uno su prefab de ítem correspondiente (VanillaItem, etc.)
//   4. Asignar Collider2D en cada uno para que OnMouseDown funcione.
//   5. El Animator compartido puede dividirse o usarse uno por fuente.
//
// IceCreamFlavor.cs también queda obsoleto — ya no es necesario.
//
// Mantener este archivo en el proyecto solo romperá la compilación si
// Unity intenta compilarlo con referencias a clases eliminadas.
// SE RECOMIENDA ELIMINAR ESTE ARCHIVO DEL PROYECTO.
// ═══════════════════════════════════════════════════════════════════

// (archivo intencionalmente vacío — ver comentario arriba)