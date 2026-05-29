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
    // Combinados (legacy — se mantienen por compatibilidad)
    WaffleWithIceCream,
    WaffleWithHoney,
    IceCreamAlone,      // Helado solo en plato
    // Contenedores
    EmptyPlate,
    Trash
}

// ─────────────────────────────────────────────────────────────────
// TIPOS DE RECETAS — todas las combinaciones posibles del GDD
//
// Nomenclatura: coincide 1:1 con PlateRecipe.ToSpriteKey()
//   - Waffle solo:        Perfect
//   - Waffle + toppings:  Perfect_<Toppings>
//   - Helado solo:        IceCream_<Sabores>
//   - Miel sola:          Honey
//
// Esta enumeración es la fuente de verdad para pedidos de clientes.
// PlateRecipe.Resolve() devuelve el valor correcto según el contenido
// del plato en el momento de la entrega.
// ─────────────────────────────────────────────────────────────────
public enum RecipeType
{
    // ── Waffle solo ──────────────────────────────────────────────
    Perfect,

    // ── Waffle + 1 topping ───────────────────────────────────────
    Perfect_Vanilla,
    Perfect_Strawberry,
    Perfect_Chocolate,
    Perfect_Honey,

    // ── Waffle + 2 toppings ──────────────────────────────────────
    Perfect_VanillaStrawberry,
    Perfect_VanillaChocolate,
    Perfect_VanillaHoney,
    Perfect_StrawberryChocolate,
    Perfect_StrawberryHoney,
    Perfect_ChocolateHoney,

    // ── Waffle + 3 toppings ──────────────────────────────────────
    Perfect_VanillaStrawberryChocolate,
    Perfect_VanillaStrawberryHoney,
    Perfect_VanillaChocolateHoney,
    Perfect_StrawberryChocolateHoney,

    // ── Waffle + 4 toppings ──────────────────────────────────────
    Perfect_VanillaStrawberryChocolateHoney,

    // ── Helado solo (sin waffle) ─────────────────────────────────
    IceCream_Vanilla,
    IceCream_Strawberry,
    IceCream_Chocolate,
    IceCream_VanillaStrawberry,
    IceCream_VanillaChocolate,
    IceCream_VanillaHoney,
    IceCream_StrawberryChocolate,
    IceCream_StrawberryHoney,
    IceCream_ChocolateHoney,
    IceCream_VanillaStrawberryChocolate,
    IceCream_VanillaStrawberryHoney,
    IceCream_VanillaChocolateHoney,
    IceCream_StrawberryChocolateHoney,
    IceCream_VanillaStrawberryChocolateHoney,

    // ── Miel sola ────────────────────────────────────────────────
    Honey,

    // ── Legacy (conservados para compatibilidad con código existente) ──
    // Se mantienen al final para no desplazar los índices de los valores anteriores.
    WaffleSimple = 100,
    IceCreamAlone = 101,
    WaffleWithIceCream = 102,
    WaffleWithHoneyButter = 103,
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