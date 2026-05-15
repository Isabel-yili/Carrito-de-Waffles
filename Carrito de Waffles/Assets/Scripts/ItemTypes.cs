using UnityEngine;

// ─────────────────────────────────────────────────────────────────
// TIPOS DE ÍTEMS — según GDD sección 10
// ─────────────────────────────────────────────────────────────────
public enum ItemType
{
    None,
    // Materias primas
    WaffleMix,          // Mezcla de waffle (recipiente)
    IceCreamVanilla,    // Helado vainilla
    IceCreamStrawberry, // Helado fresa
    IceCreamChocolate,  // Helado chocolate
    HoneyButter,        // Miel y mantequilla
    // Estados de cocción
    WaffleRaw,          // Waffle crudo (en horno)
    WaffleReady,        // Waffle listo — perfecto
    WaffleOvercooked,   // Waffle pasado — no quemado pero no ideal
    WaffleBurned,       // Waffle quemado
    // Combinados
    WaffleWithIceCream,
    WaffleWithHoney,
    IceCreamAlone,      // Helado solo en plato
    // Contenedores
    EmptyPlate,
    Trash
}

// ─────────────────────────────────────────────────────────────────
// TIPOS DE RECETAS — según GDD sección 4.3
// ─────────────────────────────────────────────────────────────────
public enum RecipeType
{
    WaffleSimple,
    IceCreamAlone,
    WaffleWithIceCream,
    WaffleWithHoneyButter
}

// ─────────────────────────────────────────────────────────────────
// INTERFAZ: Receptor de ítems (horno, plato, plataforma de entrega)
// ─────────────────────────────────────────────────────────────────
public interface IItemReceiver
{
    /// <summary>¿Puede este receptor aceptar el ítem dado?</summary>
    bool CanReceive(DraggableItem item);

    /// <summary>Procesa la recepción del ítem</summary>
    void ReceiveItem(DraggableItem item);
}

// ─────────────────────────────────────────────────────────────────
// INTERFAZ: Fuente de ítems infinita (mezcla, helados, miel)
// ─────────────────────────────────────────────────────────────────
public interface IItemSource
{
    ItemType ProducedItemType { get; }
    DraggableItem SpawnItem();
}