using UnityEngine;
using System.Collections;

// ═══════════════════════════════════════════════════════════════════
// PLATE RECIPE
// ═══════════════════════════════════════════════════════════════════

public enum WaffleCookState { None, Perfect, Overcooked, Burned }

[System.Serializable]
public class PlateRecipe
{
    public bool hasWaffle;
    public WaffleCookState cookState = WaffleCookState.None;

    public bool vanilla;
    public bool strawberry;
    public bool chocolate;
    public bool honey;

    public bool HasAnyIceCream => vanilla || strawberry || chocolate;

    /// <summary>
    /// Resuelve el RecipeType exacto según el contenido del plato.
    /// El resultado coincide 1:1 con ToSpriteKey() y con los valores
    /// del enum RecipeType definidos en ItemType.cs.
    ///
    /// REGLAS:
    ///   - Waffle quemado sin toppings → null (no es un pedido válido)
    ///   - Plato vacío → null
    ///   - Solo miel sin waffle → Honey
    ///   - El cook state NO afecta al RecipeType: los clientes piden
    ///     "Perfect_Vanilla" y el jugador puede servir ese pedido con
    ///     cualquier estado del waffle (la calidad solo afecta la recompensa
    ///     económica vía WaffleCookState en DeliveryPlatform).
    /// </summary>
    public RecipeType? Resolve()
    {
        // Plato vacío
        if (!hasWaffle && !HasAnyIceCream && !honey) return null;

        // Waffle quemado sin nada más → no entregable
        if (hasWaffle && cookState == WaffleCookState.Burned && !HasAnyIceCream && !honey)
            return null;

        // ── Sin waffle ────────────────────────────────────────────
        if (!hasWaffle)
        {
            // Solo miel (sin waffle ni helado)
            if (!HasAnyIceCream && honey) return RecipeType.Honey;

            // Helado(s) solo(s)
            if (vanilla && strawberry && chocolate && honey) return RecipeType.IceCream_VanillaStrawberryChocolateHoney;
            if (vanilla && strawberry && chocolate) return RecipeType.IceCream_VanillaStrawberryChocolate;
            if (vanilla && strawberry && honey) return RecipeType.IceCream_VanillaStrawberryHoney;
            if (vanilla && chocolate && honey) return RecipeType.IceCream_VanillaChocolateHoney;
            if (strawberry && chocolate && honey) return RecipeType.IceCream_StrawberryChocolateHoney;
            if (vanilla && strawberry) return RecipeType.IceCream_VanillaStrawberry;
            if (vanilla && chocolate) return RecipeType.IceCream_VanillaChocolate;
            if (vanilla && honey) return RecipeType.IceCream_VanillaHoney;
            if (strawberry && chocolate) return RecipeType.IceCream_StrawberryChocolate;
            if (strawberry && honey) return RecipeType.IceCream_StrawberryHoney;
            if (chocolate && honey) return RecipeType.IceCream_ChocolateHoney;
            if (vanilla) return RecipeType.IceCream_Vanilla;
            if (strawberry) return RecipeType.IceCream_Strawberry;
            if (chocolate) return RecipeType.IceCream_Chocolate;

            return null; // combinación no reconocida
        }

        // ── Con waffle ────────────────────────────────────────────
        // Waffle solo (sin toppings)
        if (!HasAnyIceCream && !honey) return RecipeType.Perfect;

        // Waffle + 4 toppings
        if (vanilla && strawberry && chocolate && honey) return RecipeType.Perfect_VanillaStrawberryChocolateHoney;

        // Waffle + 3 toppings
        if (vanilla && strawberry && chocolate) return RecipeType.Perfect_VanillaStrawberryChocolate;
        if (vanilla && strawberry && honey) return RecipeType.Perfect_VanillaStrawberryHoney;
        if (vanilla && chocolate && honey) return RecipeType.Perfect_VanillaChocolateHoney;
        if (strawberry && chocolate && honey) return RecipeType.Perfect_StrawberryChocolateHoney;

        // Waffle + 2 toppings
        if (vanilla && strawberry) return RecipeType.Perfect_VanillaStrawberry;
        if (vanilla && chocolate) return RecipeType.Perfect_VanillaChocolate;
        if (vanilla && honey) return RecipeType.Perfect_VanillaHoney;
        if (strawberry && chocolate) return RecipeType.Perfect_StrawberryChocolate;
        if (strawberry && honey) return RecipeType.Perfect_StrawberryHoney;
        if (chocolate && honey) return RecipeType.Perfect_ChocolateHoney;

        // Waffle + 1 topping
        if (vanilla) return RecipeType.Perfect_Vanilla;
        if (strawberry) return RecipeType.Perfect_Strawberry;
        if (chocolate) return RecipeType.Perfect_Chocolate;
        if (honey) return RecipeType.Perfect_Honey;

        return null;
    }

    public string ToSpriteKey()
    {
        if (!hasWaffle && !HasAnyIceCream && !honey) return "Empty";

        if (!hasWaffle)
        {
            if (!HasAnyIceCream && honey) return "Honey";
            return $"IceCream_{BuildCanonicalSuffix(vanilla, strawberry, chocolate, honey)}";
        }

        string cook = cookState switch
        {
            WaffleCookState.Perfect => "Perfect",
            WaffleCookState.Overcooked => "Overcooked",
            WaffleCookState.Burned => "Burned",
            _ => "Perfect"
        };

        if (!HasAnyIceCream && !honey) return cook;
        return $"{cook}_{BuildCanonicalSuffix(vanilla, strawberry, chocolate, honey)}";
    }

    private static string BuildCanonicalSuffix(bool v, bool s, bool c, bool h)
    {
        string result = "";
        if (v) result += "Vanilla";
        if (s) result += "Strawberry";
        if (c) result += "Chocolate";
        if (h) result += "Honey";
        return result;
    }
}

// ═══════════════════════════════════════════════════════════════════
// PLATE VISUAL ENTRY — mapeo clave → sprite
// ═══════════════════════════════════════════════════════════════════

[System.Serializable]
public class PlateVisualEntry
{
    [Tooltip("Clave canónica generada por PlateRecipe.ToSpriteKey().\n\nVer Plate.cs para lista completa de 64+ claves.")]
    public string key;
    public Sprite sprite;
}

/// <summary>
/// PLATO v5 — Fix de estado consumido post-entrega.
///
/// CAMBIO PRINCIPAL v5:
///   ConsumeAndSpawnNew() ahora marca el plate como consumido
///   (_isConsumed = true) ANTES de destruirlo, lo que hace que
///   CanReceive() devuelva false y el Update() del DraggableItem
///   no intente procesar más clicks sobre este objeto en el frame
///   que queda vivo tras llamar Destroy().
///
/// CAMBIO v5.1 (junto a RecipeType v2):
///   PlateRecipe.Resolve() devuelve ahora el RecipeType granular exacto
///   (e.g. Perfect_VanillaChocolate) en lugar de las 4 categorías antiguas.
///   Esto permite que OrderManager haga match perfecto con el pedido del cliente.
///
///   Flujo completo de entrega correcta:
///     DeliveryPlatform.OnCorrectDelivery()
///       → DragManager.OnItemReleased(item)    [limpia _hasSelectedItem]
///       → gameManager.AddMoney()
///       → plate.ConsumeAndSpawnNew()
///           → _isConsumed = true              [bloquea nuevas interacciones]
///           → Instantiate(platePrefab)        [nuevo plate en mesa]
///           → Destroy(gameObject)             [destruye este plate]
///
/// DRAG DEL PLATE:
///   persistentDrag=true  → el Plate queda donde se suelta.
///   Click derecho/Escape → vuelve al origen.
///
/// VISUAL:
///   Un solo SpriteRenderer (plateVisualRenderer), clave canónica.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(DraggableItem))]
public class Plate : MonoBehaviour, IItemReceiver
{
    // ─── Inspector ─────────────────────────────────────────────────

    [Header("Visual")]
    [Tooltip("SpriteRenderer del hijo 'PlateVisual'")]
    public SpriteRenderer plateVisualRenderer;

    [Tooltip("64 combinaciones + Empty. Clave = PlateRecipe.ToSpriteKey()")]
    public PlateVisualEntry[] plateSprites;

    [Tooltip("Sprite de fallback cuando la clave no está en la lista")]
    public Sprite spriteFallback;


    // ─── Estado ────────────────────────────────────────────────────

    private PlateRecipe _recipe = new PlateRecipe();
    private DraggableItem _myDraggable;
    private bool _isConsumed = false;  // true tras ConsumeAndSpawnNew()

    public PlateRecipe Recipe => _recipe;
    public bool IsEmpty => !_recipe.hasWaffle && !_recipe.HasAnyIceCream && !_recipe.honey;
    public bool HasCompletedRecipe => !_isConsumed && _recipe.Resolve().HasValue;
    public RecipeType? CompletedRecipe => _recipe.Resolve();
    public bool HasRecipe => HasCompletedRecipe;

    // ═══════════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {
        if (plateVisualRenderer == null)
            plateVisualRenderer =
                GetComponentInChildren<SpriteRenderer>();

        _myDraggable = GetComponent<DraggableItem>();

        _myDraggable.isDraggable = true;
        _myDraggable.holdToDrag = true;
        _myDraggable.persistentDrag = false;
        _myDraggable.destroyOnFailedDrop = false;

        UpdateVisual();
    }


    // ═══════════════════════════════════════════════════════════════
    // INPUT
    // ═══════════════════════════════════════════════════════════════

    void OnMouseDown()
    {
        if (_isConsumed) return;

        DragManager.Instance?.MarkClickHandled();

        // En MODO B (holdToDrag), el DragManager no interviene en el pickup;
        // el Plate llama directamente a OnItemPickedUp para iniciar el drag.
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem)
            return;

        DragManager.Instance?.OnItemPickedUp(_myDraggable);
    }

    // ═══════════════════════════════════════════════════════════════
    // IItemReceiver
    // ═══════════════════════════════════════════════════════════════

    public bool CanReceive(DraggableItem item)
    {
        if (_isConsumed) return false;

        switch (item.itemType)
        {
            case ItemType.WaffleReady:
            case ItemType.WaffleOvercooked:
            case ItemType.WaffleBurned:
                return !_recipe.hasWaffle;

            case ItemType.IceCreamVanilla:
                // Acepta si hay waffle, o si el plato está en modo "solo helado" (sin waffle ni miel).
                // Permite agregar múltiples sabores de helado combinados.
                return !_recipe.vanilla
                    && (_recipe.hasWaffle || !_recipe.honey);

            case ItemType.IceCreamStrawberry:
                return !_recipe.strawberry
                    && (_recipe.hasWaffle || !_recipe.honey);

            case ItemType.IceCreamChocolate:
                return !_recipe.chocolate
                    && (_recipe.hasWaffle || !_recipe.honey);

            case ItemType.HoneyButter:
                return !_recipe.honey;

            default: return false;
        }
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (!CanReceive(item)) return;

        ItemType type = item.itemType;
        item.ClearOriginOven();

        bool isWaffle = type == ItemType.WaffleReady
                     || type == ItemType.WaffleOvercooked
                     || type == ItemType.WaffleBurned;

        if (isWaffle)
        {
            item.StopCarrying();
            item.gameObject.SetActive(false);
            StartCoroutine(DestroyComponentNextFrame(item));
        }
        else
        {
            Destroy(item.gameObject);
        }

        ApplyIngredient(type);
        UpdateVisual();
        AudioManager.Instance?.PlaySound(SoundType.ItemPlaced);

        if (HasCompletedRecipe)
            OnRecipeCompleted();

        Debug.Log($"[Plate] ← {type} | clave: '{_recipe.ToSpriteKey()}' | receta: {_recipe.Resolve()}");
    }

    // ═══════════════════════════════════════════════════════════════
    // RECETA
    // ═══════════════════════════════════════════════════════════════

    private void ApplyIngredient(ItemType type)
    {
        switch (type)
        {
            case ItemType.WaffleReady: _recipe.hasWaffle = true; _recipe.cookState = WaffleCookState.Perfect; break;
            case ItemType.WaffleOvercooked: _recipe.hasWaffle = true; _recipe.cookState = WaffleCookState.Overcooked; break;
            case ItemType.WaffleBurned: _recipe.hasWaffle = true; _recipe.cookState = WaffleCookState.Burned; break;
            case ItemType.IceCreamVanilla: _recipe.vanilla = true; break;
            case ItemType.IceCreamStrawberry: _recipe.strawberry = true; break;
            case ItemType.IceCreamChocolate: _recipe.chocolate = true; break;
            case ItemType.HoneyButter: _recipe.honey = true; break;
        }
    }

    private void OnRecipeCompleted()
    {
        FeedbackManager.Instance?.ShowRecipeComplete(transform.position);
        AudioManager.Instance?.PlaySound(SoundType.RecipeComplete);
        Debug.Log($"[Plate] ✅ Receta: {_recipe.Resolve()} | calidad: {_recipe.cookState}");
    }

    // ═══════════════════════════════════════════════════════════════
    // VISUAL
    // ═══════════════════════════════════════════════════════════════

    private void UpdateVisual()
    {
        if (plateVisualRenderer == null)
        {
            Debug.LogWarning("[Plate] plateVisualRenderer no asignado.");
            return;
        }

        string key = _recipe.ToSpriteKey();
        Sprite sprite = FindSprite(key);

        if (sprite != null)
        {
            plateVisualRenderer.sprite = sprite;
        }
        else
        {
            plateVisualRenderer.sprite = spriteFallback;
            Debug.LogWarning($"[Plate] Sprite no encontrado para '{key}' — usando fallback.");
        }
    }

    private Sprite FindSprite(string key)
    {
        if (plateSprites == null) return null;
        foreach (var entry in plateSprites)
            if (entry.key == key) return entry.sprite;
        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    // CONSUME — llamado SOLO por DeliveryPlatform.OnCorrectDelivery()
    // ═══════════════════════════════════════════════════════════════

    public void ConsumeAndSpawnNew()
    {
        _isConsumed = true;

        if (_myDraggable != null)
        {
            _myDraggable.StopCarrying();
        }

        PlateSpawner.Instance?.SpawnPlate();

        Destroy(gameObject);
    }

    // ═══════════════════════════════════════════════════════════════
    // RESET
    // ═══════════════════════════════════════════════════════════════

    public void ClearPlate()
    {
        _recipe = new PlateRecipe();
        _isConsumed = false;
        UpdateVisual();
        Debug.Log("[Plate] Limpiado.");
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator DestroyComponentNextFrame(DraggableItem di)
    {
        yield return null;
        if (di != null) Destroy(di);
    }

    // ═══════════════════════════════════════════════════════════════
    // EDITOR
    // ═══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    [Header("— Editor / Debug —")]
    public string debugPreviewKey = "Perfect_VanillaChocolate";

    [ContextMenu("Preview Sprite")]
    private void EditorPreviewSprite()
    {
        if (plateVisualRenderer == null) { Debug.LogWarning("[Plate] plateVisualRenderer no asignado."); return; }
        Sprite s = FindSprite(debugPreviewKey);
        plateVisualRenderer.sprite = s != null ? s : spriteFallback;
        Debug.Log(s != null
            ? $"[Plate] Preview OK: '{debugPreviewKey}'"
            : $"[Plate] Clave '{debugPreviewKey}' no encontrada.");
    }

    [ContextMenu("Log All Defined Keys")]
    private void EditorLogAllKeys()
    {
        if (plateSprites == null || plateSprites.Length == 0) { Debug.Log("[Plate] plateSprites[] vacío."); return; }
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Plate] {plateSprites.Length} claves definidas:");
        foreach (var e in plateSprites)
            sb.AppendLine($"  '{e.key}' → {(e.sprite != null ? e.sprite.name : "NULL")}");
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Validate All 64 Keys")]
    private void EditorValidateAllKeys()
    {
        string[] canonical = {
            "Empty",
            "Perfect", "Overcooked", "Burned",
            "Perfect_Vanilla","Perfect_Strawberry","Perfect_Chocolate","Perfect_Honey",
            "Overcooked_Vanilla","Overcooked_Strawberry","Overcooked_Chocolate","Overcooked_Honey",
            "Burned_Vanilla","Burned_Strawberry","Burned_Chocolate","Burned_Honey",
            "Perfect_VanillaStrawberry","Perfect_VanillaChocolate","Perfect_VanillaHoney",
            "Perfect_StrawberryChocolate","Perfect_StrawberryHoney","Perfect_ChocolateHoney",
            "Overcooked_VanillaStrawberry","Overcooked_VanillaChocolate","Overcooked_VanillaHoney",
            "Overcooked_StrawberryChocolate","Overcooked_StrawberryHoney","Overcooked_ChocolateHoney",
            "Burned_VanillaStrawberry","Burned_VanillaChocolate","Burned_VanillaHoney",
            "Burned_StrawberryChocolate","Burned_StrawberryHoney","Burned_ChocolateHoney",
            "Perfect_VanillaStrawberryChocolate","Perfect_VanillaStrawberryHoney",
            "Perfect_VanillaChocolateHoney","Perfect_StrawberryChocolateHoney",
            "Overcooked_VanillaStrawberryChocolate","Overcooked_VanillaStrawberryHoney",
            "Overcooked_VanillaChocolateHoney","Overcooked_StrawberryChocolateHoney",
            "Burned_VanillaStrawberryChocolate","Burned_VanillaStrawberryHoney",
            "Burned_VanillaChocolateHoney","Burned_StrawberryChocolateHoney",
            "Perfect_VanillaStrawberryChocolateHoney",
            "Overcooked_VanillaStrawberryChocolateHoney",
            "Burned_VanillaStrawberryChocolateHoney",
            "IceCream_Vanilla","IceCream_Strawberry","IceCream_Chocolate",
            "IceCream_VanillaStrawberry","IceCream_VanillaChocolate","IceCream_VanillaHoney",
            "IceCream_StrawberryChocolate","IceCream_StrawberryHoney","IceCream_ChocolateHoney",
            "IceCream_VanillaStrawberryChocolate","IceCream_VanillaStrawberryHoney",
            "IceCream_VanillaChocolateHoney","IceCream_StrawberryChocolateHoney",
            "IceCream_VanillaStrawberryChocolateHoney",
            "Honey"
        };

        int missing = 0, nullSprite = 0;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Plate] Validando {canonical.Length} claves canónicas:");

        foreach (string k in canonical)
        {
            Sprite s = FindSprite(k);
            if (s == null)
            {
                bool keyExists = false;
                if (plateSprites != null)
                    foreach (var e in plateSprites)
                        if (e.key == k) { keyExists = true; break; }

                if (!keyExists) { sb.AppendLine($"  ❌ FALTA clave: '{k}'"); missing++; }
                else { sb.AppendLine($"  ⚠️  Clave '{k}' existe pero sprite es NULL"); nullSprite++; }
            }
        }

        if (missing == 0 && nullSprite == 0)
            sb.AppendLine("  ✅ Todas las claves tienen sprite asignado.");
        else
            sb.AppendLine($"\n  Resumen: {missing} claves faltantes, {nullSprite} sprites null.");

        Debug.Log(sb.ToString());
    }
#endif
}