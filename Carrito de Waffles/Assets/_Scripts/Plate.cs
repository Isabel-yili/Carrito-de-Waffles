using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// PLATO — GDD secciones 4.3 y 4.4
///
/// FLUJO ACTUALIZADO:
///   1. El plato empieza vacío y NO es arrastrable.
///   2. El jugador extrae un Waffle del Oven (ahora es un DraggableItem
///      independiente en la escena).
///   3. El jugador hace click sobre el Plate para entregarle el Waffle.
///      → Plate.CanReceive() acepta WaffleReady y WaffleOvercooked.
///      → Al recibirlo, limpia la referencia al horno (ClearOriginOven)
///        para que el horno ya no lo "reclame" si se cancela.
///   4. Con el Waffle en el plato, el jugador puede añadir helados o miel
///      haciendo click sobre sus fuentes (ItemSource).
///   5. Al completar la receta, el Plate se vuelve arrastrable (isDraggable = true).
///   6. El jugador arrastra el Plate terminado a la DeliveryPlatform.
///      Si lo suelta en un lugar incorrecto, el Plate vuelve a su posición
///      original gracias a DraggableItem.ReturnToOrigin().
///
/// JERARQUÍA DEL PREFAB:
///   Plate  (Plate + DraggableItem + Collider2D)
///   └── ingredientSlots[]  → SpriteRenderers de los ingredientes apilados
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(DraggableItem))]
public class Plate : MonoBehaviour, IItemReceiver
{
    [Header("Contenido Visual")]
    [Tooltip("SpriteRenderers superpuestos en el plato, uno por capa de ingrediente")]
    public List<SpriteRenderer> ingredientSlots;

    [Header("Sprites de ingredientes")]
    [Tooltip("Sprites en el mismo orden que ItemType para renderizar el contenido")]
    public Sprite spriteWaffleReady;
    public Sprite spriteWaffleOvercooked;
    public Sprite spriteIceCreamVanilla;
    public Sprite spriteIceCreamStrawberry;
    public Sprite spriteIceCreamChocolate;
    public Sprite spriteHoneyButter;

    // ─── Estado del plato ─────────────────────────────────────────
    private List<ItemType> _contents = new List<ItemType>();
    private RecipeType? _completedRecipe = null;
    private DraggableItem _myDraggable;

    public bool IsEmpty => _contents.Count == 0;
    public bool HasRecipe => _completedRecipe.HasValue;
    public bool UsedOvercookedWaffle => _contents.Contains(ItemType.WaffleOvercooked);
    public RecipeType? CompletedRecipe => _completedRecipe;

    void Awake()
    {
        _myDraggable = GetComponent<DraggableItem>();
        // El plato vacío NO es arrastrable
        _myDraggable.isDraggable = false;
    }

    // ─────────────────────────────────────────────────────────────
    // CLICK SOBRE EL PLATO — el plato actúa como receptor de
    // ingredientes (si no tiene receta) o como ítem arrastrable
    // (si ya tiene receta completa).
    // ─────────────────────────────────────────────────────────────

    void OnMouseDown()
    {
        // Si el plato ya tiene receta completa y el jugador no lleva nada,
        // activar el modo arrastrar del propio plato
        if (HasRecipe && DragManager.Instance != null && !DragManager.Instance.HasSelectedItem)
        {
            DragManager.Instance.SelectItem(_myDraggable);
            return;
        }

        // Si el jugador lleva un ítem y este plato puede recibirlo,
        // el DragManager.TryInteractWith lo maneja; aquí solo fallback
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem)
        {
            DragManager.Instance.TryInteractWith(_myDraggable);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // IItemReceiver — recibe Waffles e ingredientes
    // ─────────────────────────────────────────────────────────────

    public bool CanReceive(DraggableItem item)
    {
        // No acepta nada si ya tiene una receta completa
        if (_completedRecipe.HasValue) return false;

        return item.itemType switch
        {
            // Waffles del horno
            ItemType.WaffleReady =>
                !_contents.Contains(ItemType.WaffleReady)
                && !_contents.Contains(ItemType.WaffleOvercooked),

            ItemType.WaffleOvercooked =>
                !_contents.Contains(ItemType.WaffleReady)
                && !_contents.Contains(ItemType.WaffleOvercooked),

            // Waffles quemados: el plato los acepta para poder tirarlos
            // (el jugador puede mandar el plato a la basura)
            ItemType.WaffleBurned =>
                !_contents.Contains(ItemType.WaffleReady)
                && !_contents.Contains(ItemType.WaffleOvercooked)
                && !_contents.Contains(ItemType.WaffleBurned),

            // Helados: solo si hay waffle en el plato o el plato está vacío
            // (helado solo también es un pedido válido)
            ItemType.IceCreamVanilla =>
                _contents.Count == 0
                || _contents.Contains(ItemType.WaffleReady)
                || _contents.Contains(ItemType.WaffleOvercooked),

            ItemType.IceCreamStrawberry =>
                _contents.Count == 0
                || _contents.Contains(ItemType.WaffleReady)
                || _contents.Contains(ItemType.WaffleOvercooked),

            ItemType.IceCreamChocolate =>
                _contents.Count == 0
                || _contents.Contains(ItemType.WaffleReady)
                || _contents.Contains(ItemType.WaffleOvercooked),

            // Miel: solo si hay waffle (no sobre helado solo)
            ItemType.HoneyButter =>
                _contents.Contains(ItemType.WaffleReady)
                || _contents.Contains(ItemType.WaffleOvercooked),

            _ => false
        };
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (!CanReceive(item)) return;

        ItemType receivedType = item.itemType;

        // Limpiar referencia al horno — el waffle ya no pertenece a ninguna wafflera
        item.ClearOriginOven();

        // El item.gameObject puede ser el WaffleDisplay de la wafflera (no un prefab
        // instanciado), por lo que NO lo destruimos: solo quitamos el DraggableItem
        // añadido en runtime. Si es un item normal (icecream, miel), si lo destruimos.
        bool isWaffleFromOven = receivedType == ItemType.WaffleReady
                             || receivedType == ItemType.WaffleOvercooked
                             || receivedType == ItemType.WaffleBurned;

        if (isWaffleFromOven)
        {
            // Ocultar el WaffleDisplay: la visual del ingrediente la maneja ingredientSlots
            item.StopCarrying();
            item.gameObject.SetActive(false);
            // Destruir solo el componente DraggableItem (un frame despues para no
            // interrumpir el callstack actual)
            StartCoroutine(DestroyComponentNextFrame(item));
        }
        else
        {
            Destroy(item.gameObject);
        }

        _contents.Add(receivedType);

        UpdateVisual();
        CheckRecipeCompletion();
        AudioManager.Instance?.PlaySound(SoundType.ItemPlaced);
    }

    private System.Collections.IEnumerator DestroyComponentNextFrame(DraggableItem di)
    {
        yield return null;
        if (di != null) Destroy(di);
    }

    // ─────────────────────────────────────────────────────────────
    // RECETAS — según GDD sección 4.3
    // ─────────────────────────────────────────────────────────────

    private void CheckRecipeCompletion()
    {
        // Helado solo (sin waffle)
        if (_contents.Count == 1 && IsIceCream(_contents[0]))
        {
            CompleteRecipe(RecipeType.IceCreamAlone);
            return;
        }

        // Waffle simple (solo waffle, sin toppings)
        if (_contents.Count == 1
            && (_contents[0] == ItemType.WaffleReady || _contents[0] == ItemType.WaffleOvercooked))
        {
            CompleteRecipe(RecipeType.WaffleSimple);
            return;
        }

        // Waffle con helado
        bool hasWaffle = _contents.Contains(ItemType.WaffleReady) || _contents.Contains(ItemType.WaffleOvercooked);
        bool hasIceCream = _contents.Exists(IsIceCream);

        if (hasWaffle && hasIceCream)
        {
            CompleteRecipe(RecipeType.WaffleWithIceCream);
            return;
        }

        // Waffle con miel y mantequilla
        if (hasWaffle && _contents.Contains(ItemType.HoneyButter))
        {
            CompleteRecipe(RecipeType.WaffleWithHoneyButter);
            return;
        }
    }

    private void CompleteRecipe(RecipeType recipe)
    {
        _completedRecipe = recipe;

        // El plato ahora es arrastrable para llevarlo a la plataforma de entrega
        _myDraggable.isDraggable = true;

        FeedbackManager.Instance?.ShowRecipeComplete(transform.position);
        AudioManager.Instance?.PlaySound(SoundType.RecipeComplete);

        Debug.Log($"[Plate] Receta completada: {recipe}");
    }

    private bool IsIceCream(ItemType t) =>
        t == ItemType.IceCreamVanilla ||
        t == ItemType.IceCreamStrawberry ||
        t == ItemType.IceCreamChocolate;

    // ─────────────────────────────────────────────────────────────
    // VISUAL — actualizar sprites de ingredientes
    // ─────────────────────────────────────────────────────────────

    private void UpdateVisual()
    {
        // Limpiar todos los slots primero
        foreach (var sr in ingredientSlots)
            if (sr != null) sr.sprite = null;

        // Asignar sprites según el contenido actual
        for (int i = 0; i < _contents.Count && i < ingredientSlots.Count; i++)
        {
            if (ingredientSlots[i] == null) continue;

            ingredientSlots[i].sprite = _contents[i] switch
            {
                ItemType.WaffleReady => spriteWaffleReady,
                ItemType.WaffleOvercooked => spriteWaffleOvercooked,
                ItemType.IceCreamVanilla => spriteIceCreamVanilla,
                ItemType.IceCreamStrawberry => spriteIceCreamStrawberry,
                ItemType.IceCreamChocolate => spriteIceCreamChocolate,
                ItemType.HoneyButter => spriteHoneyButter,
                _ => null
            };
        }

        Debug.Log($"[Plate] Contenido: {string.Join(", ", _contents)}");
    }

    // ─────────────────────────────────────────────────────────────
    // RESET — al tirar el plato a la basura o al rechazarlo
    // ─────────────────────────────────────────────────────────────

    public void ClearPlate()
    {
        _contents.Clear();
        _completedRecipe = null;
        _myDraggable.isDraggable = false;
        UpdateVisual();
    }
}