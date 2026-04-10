using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// PLATO — GDD secciones 4.3 y 4.4
/// Recibe ítems, los combina según las reglas de recetas,
/// y permite llevarlo a la plataforma de entrega.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(DraggableItem))]
public class Plate : MonoBehaviour, IItemReceiver
{
    [Header("Contenido Visual")]
    public List<SpriteRenderer> ingredientSlots; // Sprites superpuestos en el plato

    // ─── Estado del plato ─────────────────────────────────────────
    private List<ItemType> _contents = new List<ItemType>();
    private RecipeType? _completedRecipe = null;
    private DraggableItem _myDraggable;

    public bool IsEmpty => _contents.Count == 0;
    public bool HasRecipe => _completedRecipe.HasValue;
    public RecipeType? CompletedRecipe => _completedRecipe;

    void Awake()
    {
        _myDraggable = GetComponent<DraggableItem>();
        // Plato vacío no es draggable hasta que tenga contenido
        _myDraggable.isDraggable = false;
    }

    // ─────────────────────────────────────────────────────────────
    // IItemReceiver
    // ─────────────────────────────────────────────────────────────

    public bool CanReceive(DraggableItem item)
    {
        // El plato acepta ítems mientras no tenga una receta completa
        if (_completedRecipe.HasValue) return false;

        return item.itemType switch
        {
            ItemType.WaffleReady        => !_contents.Contains(ItemType.WaffleReady),
            ItemType.IceCreamVanilla    => _contents.Count == 0 || _contents.Contains(ItemType.WaffleReady),
            ItemType.IceCreamStrawberry => _contents.Count == 0 || _contents.Contains(ItemType.WaffleReady),
            ItemType.IceCreamChocolate  => _contents.Count == 0 || _contents.Contains(ItemType.WaffleReady),
            ItemType.HoneyButter        => _contents.Contains(ItemType.WaffleReady),
            _                           => false
        };
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (!CanReceive(item)) return;

        _contents.Add(item.itemType);
        Destroy(item.gameObject); // El ícono se "funde" en el plato

        UpdateVisual();
        CheckRecipeCompletion();
        AudioManager.Instance?.PlaySound(SoundType.ItemPlaced);
    }

    // ─────────────────────────────────────────────────────────────
    // RECETAS — según GDD sección 4.3
    // ─────────────────────────────────────────────────────────────

    private void CheckRecipeCompletion()
    {
        // Helado solo (vainilla, fresa o chocolate sin waffle)
        if (_contents.Count == 1 && IsIceCream(_contents[0]))
        {
            CompleteRecipe(RecipeType.IceCreamAlone);
            return;
        }

        // Waffle simple (solo waffle, sin toppings)
        if (_contents.Count == 1 && _contents[0] == ItemType.WaffleReady)
        {
            CompleteRecipe(RecipeType.WaffleSimple);
            return;
        }

        // Waffle con helado
        if (_contents.Contains(ItemType.WaffleReady) && _contents.Exists(IsIceCream))
        {
            CompleteRecipe(RecipeType.WaffleWithIceCream);
            return;
        }

        // Waffle con miel y mantequilla
        if (_contents.Contains(ItemType.WaffleReady) && _contents.Contains(ItemType.HoneyButter))
        {
            CompleteRecipe(RecipeType.WaffleWithHoneyButter);
            return;
        }
    }

    private void CompleteRecipe(RecipeType recipe)
    {
        _completedRecipe = recipe;
        // El plato ahora es draggable para llevarlo a la plataforma de entrega
        _myDraggable.isDraggable = true;

        FeedbackManager.Instance?.ShowRecipeComplete(transform.position);
        AudioManager.Instance?.PlaySound(SoundType.RecipeComplete);
    }

    private bool IsIceCream(ItemType t) =>
        t == ItemType.IceCreamVanilla ||
        t == ItemType.IceCreamStrawberry ||
        t == ItemType.IceCreamChocolate;

    // ─────────────────────────────────────────────────────────────
    // VISUAL
    // ─────────────────────────────────────────────────────────────

    private void UpdateVisual()
    {
        // Actualizar sprites superpuestos según contenido
        // (En prototipo: cambiar color/sprite del plato)
        // Los sprites reales se asignarán con assets del juego
        Debug.Log($"[Plate] Contenido: {string.Join(", ", _contents)}");
    }

    // ─────────────────────────────────────────────────────────────
    // RESET (al ir a la basura o ser rechazado)
    // ─────────────────────────────────────────────────────────────

    public void ClearPlate()
    {
        _contents.Clear();
        _completedRecipe = null;
        _myDraggable.isDraggable = false;
        UpdateVisual();
    }
}
