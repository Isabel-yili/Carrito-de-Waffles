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

    // Toppings — el ORDEN aquí NO importa; ToSpriteKey() siempre los normaliza.
    public bool vanilla;
    public bool strawberry;
    public bool chocolate;
    public bool honey;

    public bool HasAnyIceCream => vanilla || strawberry || chocolate;

    // ─────────────────────────────────────────────────────────────
    // RESOLVE — qué RecipeType representa esta combinación
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve el RecipeType completado, o null si el plato no tiene aún
    /// una receta entregable (vacío, o waffle quemado sin toppings).
    /// </summary>
    public RecipeType? Resolve()
    {
        if (!hasWaffle && !HasAnyIceCream && !honey) return null;

        // Waffle quemado sin toppings → no entregable al delivery
        if (hasWaffle && cookState == WaffleCookState.Burned && !HasAnyIceCream && !honey)
            return null;

        if (!hasWaffle && (HasAnyIceCream || honey)) return RecipeType.IceCreamAlone;
        if (hasWaffle && !HasAnyIceCream && !honey) return RecipeType.WaffleSimple;
        if (hasWaffle && HasAnyIceCream) return RecipeType.WaffleWithIceCream;
        if (hasWaffle && honey && !HasAnyIceCream) return RecipeType.WaffleWithHoneyButter;

        return null;
    }

    // ─────────────────────────────────────────────────────────────
    // TO SPRITE KEY — convención CANÓNICA de claves
    // ─────────────────────────────────────────────────────────────
    //
    // REGLAS OBLIGATORIAS:
    //   1. Orden fijo de ingredientes: Vanilla → Strawberry → Chocolate → Honey
    //      independientemente del orden en que el jugador los agregó.
    //   2. Formato con waffle:    [CookState]_[Ingredientes]
    //      Formato sin waffle:   IceCream_[Ingredientes]
    //      Solo honey sin waffle: Honey
    //   3. Sin toppings:          Perfect | Overcooked | Burned
    //   4. Plato vacío:           Empty
    //
    // EJEMPLOS:
    //   Waffle perfecto solo               → "Perfect"
    //   Waffle overcooked + vainilla       → "Overcooked_Vanilla"
    //   Waffle perfecto + chocolate + miel → "Perfect_ChocolateHoney"
    //   Chocolate + vainilla (sin waffle)  → "IceCream_VanillaChocolate"  ← SIEMPRE Vanilla primero
    //   Solo miel (sin waffle, edge case)  → "Honey"
    //   Plato vacío                        → "Empty"

    public string ToSpriteKey()
    {
        // ── Plato vacío ───────────────────────────────────────────
        if (!hasWaffle && !HasAnyIceCream && !honey)
            return "Empty";

        // ── Sin waffle ────────────────────────────────────────────
        if (!hasWaffle)
        {
            // Solo miel (edge case)
            if (!HasAnyIceCream && honey) return "Honey";

            // Helados ± miel
            string suffix = BuildCanonicalSuffix(vanilla, strawberry, chocolate, honey);
            return $"IceCream_{suffix}";
        }

        // ── Con waffle ────────────────────────────────────────────
        string cook = cookState switch
        {
            WaffleCookState.Perfect => "Perfect",
            WaffleCookState.Overcooked => "Overcooked",
            WaffleCookState.Burned => "Burned",
            _ => "Perfect"
        };

        // Sin toppings
        if (!HasAnyIceCream && !honey) return cook;

        // Con toppings
        string toppings = BuildCanonicalSuffix(vanilla, strawberry, chocolate, honey);
        return $"{cook}_{toppings}";
    }

    // ─────────────────────────────────────────────────────────────
    // HELPER — construye el sufijo en ORDEN CANÓNICO
    //   Vanilla → Strawberry → Chocolate → Honey
    // Este método es la única fuente de verdad del orden.
    // ─────────────────────────────────────────────────────────────

    private static string BuildCanonicalSuffix(
        bool v, bool s, bool c, bool h)
    {
        // System.Text.StringBuilder no disponible sin using — usar concatenación simple.
        // El resultado siempre tiene el mismo orden, sin importar el estado de los flags.
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
    [Tooltip(
        "Clave canónica generada por PlateRecipe.ToSpriteKey().\n\n" +
        "LISTA COMPLETA (64 combinaciones + Empty):\n\n" +

        "── VACÍO ──────────────────────\n" +
        "Empty\n\n" +

        "── A: SOLO WAFFLE ─────────────\n" +
        "Perfect  |  Overcooked  |  Burned\n\n" +

        "── B: WAFFLE + 1 INGREDIENTE ──\n" +
        "Perfect_Vanilla         Overcooked_Vanilla         Burned_Vanilla\n" +
        "Perfect_Strawberry      Overcooked_Strawberry      Burned_Strawberry\n" +
        "Perfect_Chocolate       Overcooked_Chocolate       Burned_Chocolate\n" +
        "Perfect_Honey           Overcooked_Honey           Burned_Honey\n\n" +

        "── C: WAFFLE + 2 INGREDIENTES ─\n" +
        "Perfect_VanillaStrawberry      Overcooked_VanillaStrawberry      Burned_VanillaStrawberry\n" +
        "Perfect_VanillaChocolate       Overcooked_VanillaChocolate       Burned_VanillaChocolate\n" +
        "Perfect_VanillaHoney           Overcooked_VanillaHoney           Burned_VanillaHoney\n" +
        "Perfect_StrawberryChocolate    Overcooked_StrawberryChocolate    Burned_StrawberryChocolate\n" +
        "Perfect_StrawberryHoney        Overcooked_StrawberryHoney        Burned_StrawberryHoney\n" +
        "Perfect_ChocolateHoney         Overcooked_ChocolateHoney         Burned_ChocolateHoney\n\n" +

        "── D: WAFFLE + 3 INGREDIENTES ─\n" +
        "Perfect_VanillaStrawberryChocolate    Overcooked_VanillaStrawberryChocolate    Burned_VanillaStrawberryChocolate\n" +
        "Perfect_VanillaStrawberryHoney        Overcooked_VanillaStrawberryHoney        Burned_VanillaStrawberryHoney\n" +
        "Perfect_VanillaChocolateHoney         Overcooked_VanillaChocolateHoney         Burned_VanillaChocolateHoney\n" +
        "Perfect_StrawberryChocolateHoney      Overcooked_StrawberryChocolateHoney      Burned_StrawberryChocolateHoney\n\n" +

        "── E: WAFFLE + TODOS ──────────\n" +
        "Perfect_VanillaStrawberryChocolateHoney\n" +
        "Overcooked_VanillaStrawberryChocolateHoney\n" +
        "Burned_VanillaStrawberryChocolateHoney\n\n" +

        "── F: HELADOS SIN WAFFLE ──────\n" +
        "IceCream_Vanilla            IceCream_Strawberry           IceCream_Chocolate\n" +
        "IceCream_VanillaStrawberry  IceCream_VanillaChocolate     IceCream_VanillaHoney\n" +
        "IceCream_StrawberryChocolate  IceCream_StrawberryHoney    IceCream_ChocolateHoney\n" +
        "IceCream_VanillaStrawberryChocolate  IceCream_VanillaStrawberryHoney\n" +
        "IceCream_VanillaChocolateHoney  IceCream_StrawberryChocolateHoney\n" +
        "IceCream_VanillaStrawberryChocolateHoney\n" +
        "Honey   (solo miel, sin waffle ni helado — edge case)")]
    public string key;
    public Sprite sprite;
}

/// <summary>
/// PLATO v4 — Arquitectura visual por sprites únicos.
///
/// PRINCIPIO CENTRAL:
///   El Plate NO contiene objetos físicos de ingredientes.
///   Cuando recibe un ingrediente:
///     1. Guarda el estado en PlateRecipe (hasWaffle, cookState, vanilla, etc.).
///     2. Destruye el DraggableItem recibido (o desactiva el WaffleDisplay del Oven).
///     3. Llama UpdateVisual() → genera la clave canónica → cambia UN SpriteRenderer.
///
/// REGLA DE ORO:
///   La clave siempre sigue el orden Vanilla → Strawberry → Chocolate → Honey,
///   sin importar el orden en que el jugador los agregó.
///
/// JERARQUÍA DEL PREFAB:
///   Plate           ← Plate + DraggableItem + Collider2D
///   └── PlateVisual ← SpriteRenderer (asignar a plateVisualRenderer)
///
/// SETUP DE SPRITES:
///   Llenar "plateSprites" con las 64 combinaciones + "Empty".
///   Si una clave no tiene sprite asignado, se muestra spriteFallback
///   y aparece un warning en consola con la clave exacta faltante.
///
/// DRAG DEL PLATE:
///   persistentDrag = true → el Plate se queda donde se suelta.
///   Click derecho / Escape → vuelve al origen.
///
/// SPAWN DE NUEVO PLATE:
///   SOLO ocurre cuando DeliveryPlatform confirma entrega correcta
///   y llama ConsumeAndSpawnNew().
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(DraggableItem))]
public class Plate : MonoBehaviour, IItemReceiver
{
    // ─── Inspector ─────────────────────────────────────────────────

    [Header("Visual")]
    [Tooltip("SpriteRenderer del hijo 'PlateVisual' — muestra el estado actual")]
    public SpriteRenderer plateVisualRenderer;

    [Tooltip("64 combinaciones + Empty. Clave = PlateRecipe.ToSpriteKey()")]
    public PlateVisualEntry[] plateSprites;

    [Tooltip("Sprite de fallback cuando la clave no está en la lista (útil en desarrollo)")]
    public Sprite spriteFallback;

    [Header("Spawn del próximo plato")]
    [Tooltip("Prefab de este mismo Plate")]
    public GameObject platePrefab;
    [Tooltip("Transform de la mesa donde aparece el nuevo plato tras entrega correcta")]
    public Transform plateSpawnPoint;

    // ─── Estado ────────────────────────────────────────────────────

    private PlateRecipe _recipe = new PlateRecipe();
    private DraggableItem _myDraggable;

    // Propiedades públicas
    public PlateRecipe Recipe => _recipe;
    public bool IsEmpty => !_recipe.hasWaffle && !_recipe.HasAnyIceCream && !_recipe.honey;
    public bool HasCompletedRecipe => _recipe.Resolve().HasValue;
    public RecipeType? CompletedRecipe => _recipe.Resolve();
    public bool HasRecipe => HasCompletedRecipe;   // compatibilidad DeliveryPlatform

    // ═══════════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {
        _myDraggable = GetComponent<DraggableItem>();

        // MODO B — Persistent Drag
        _myDraggable.isDraggable = true;
        _myDraggable.persistentDrag = true;
        _myDraggable.destroyOnFailedDrop = false;

        UpdateVisual();
    }

    // ═══════════════════════════════════════════════════════════════
    // INPUT
    // ═══════════════════════════════════════════════════════════════

    void OnMouseDown()
    {
        DragManager.Instance?.MarkClickHandled();

        // Si el jugador lleva algo, el Update() de ese DraggableItem procesará
        // la entrega vía TryDeliverToTarget(). No duplicar aquí.
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem)
            return;

        // Sin item en mano → tomar el Plate
        DragManager.Instance?.SelectItem(_myDraggable);
    }

    // ═══════════════════════════════════════════════════════════════
    // IItemReceiver
    // ═══════════════════════════════════════════════════════════════

    public bool CanReceive(DraggableItem item)
    {
        switch (item.itemType)
        {
            // Waffles: solo uno
            case ItemType.WaffleReady:
            case ItemType.WaffleOvercooked:
            case ItemType.WaffleBurned:
                return !_recipe.hasWaffle;

            // Helados: con waffle, o plato totalmente vacío (helado solo)
            case ItemType.IceCreamVanilla:
                return !_recipe.vanilla
                    && (_recipe.hasWaffle || (!_recipe.HasAnyIceCream && !_recipe.honey));

            case ItemType.IceCreamStrawberry:
                return !_recipe.strawberry
                    && (_recipe.hasWaffle || (!_recipe.HasAnyIceCream && !_recipe.honey));

            case ItemType.IceCreamChocolate:
                return !_recipe.chocolate
                    && (_recipe.hasWaffle || (!_recipe.HasAnyIceCream && !_recipe.honey));

            // Miel: solo sobre waffle
            case ItemType.HoneyButter:
                return !_recipe.honey && _recipe.hasWaffle;

            default: return false;
        }
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (!CanReceive(item)) return;

        ItemType type = item.itemType;
        item.ClearOriginOven();

        // ── Destruir el item visual del cursor ────────────────────
        bool isWaffle = type == ItemType.WaffleReady
                     || type == ItemType.WaffleOvercooked
                     || type == ItemType.WaffleBurned;

        if (isWaffle)
        {
            // WaffleDisplay pertenece al Oven — solo ocultarlo
            item.StopCarrying();
            item.gameObject.SetActive(false);
            StartCoroutine(DestroyComponentNextFrame(item));
        }
        else
        {
            // Ingrediente temporal instanciado → destruir objeto completo
            Destroy(item.gameObject);
        }

        // ── Actualizar receta ─────────────────────────────────────
        ApplyIngredient(type);

        // ── Cambiar sprite — UN solo SpriteRenderer ───────────────
        UpdateVisual();

        AudioManager.Instance?.PlaySound(SoundType.ItemPlaced);

        if (HasCompletedRecipe)
            OnRecipeCompleted();

        Debug.Log($"[Plate] ← {type} | clave: '{_recipe.ToSpriteKey()}' | receta: {_recipe.Resolve()}");
    }

    // ═══════════════════════════════════════════════════════════════
    // RECETA — lógica interna
    // ═══════════════════════════════════════════════════════════════

    private void ApplyIngredient(ItemType type)
    {
        switch (type)
        {
            case ItemType.WaffleReady:
                _recipe.hasWaffle = true;
                _recipe.cookState = WaffleCookState.Perfect; break;
            case ItemType.WaffleOvercooked:
                _recipe.hasWaffle = true;
                _recipe.cookState = WaffleCookState.Overcooked; break;
            case ItemType.WaffleBurned:
                _recipe.hasWaffle = true;
                _recipe.cookState = WaffleCookState.Burned; break;
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
    // VISUAL — UN SpriteRenderer, clave canónica
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
            // Warning explícito con la clave exacta que falta — facilita añadir assets
            Debug.LogWarning($"[Plate] Sprite no encontrado para '{key}' — usando fallback. " +
                             $"Añadir entrada con esa clave exacta en plateSprites[].");
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
    // SPAWN DE NUEVO PLATE — solo tras entrega correcta
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Llamado EXCLUSIVAMENTE por DeliveryPlatform.OnCorrectDelivery().
    /// Instancia un nuevo Plate en la mesa y destruye este.
    /// </summary>
    public void ConsumeAndSpawnNew()
    {
        if (platePrefab != null && plateSpawnPoint != null)
        {
            Instantiate(platePrefab, plateSpawnPoint.position, Quaternion.identity);
            Debug.Log("[Plate] Nuevo plato en mesa.");
        }
        else
        {
            Debug.LogWarning("[Plate] platePrefab o plateSpawnPoint no asignados.");
        }
        Destroy(gameObject);
    }

    // ═══════════════════════════════════════════════════════════════
    // RESET
    // ═══════════════════════════════════════════════════════════════

    public void ClearPlate()
    {
        _recipe = new PlateRecipe();
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
    // EDITOR — utilidades de desarrollo
    // ═══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    [Header("— Editor / Debug —")]
    [Tooltip("Clave a previsualizar. Pulsar 'Preview Sprite' en el menú contextual del componente.")]
    public string debugPreviewKey = "Perfect_VanillaChocolate";

    [ContextMenu("Preview Sprite")]
    private void EditorPreviewSprite()
    {
        if (plateVisualRenderer == null)
        {
            Debug.LogWarning("[Plate] plateVisualRenderer no asignado.");
            return;
        }
        Sprite s = FindSprite(debugPreviewKey);
        plateVisualRenderer.sprite = s != null ? s : spriteFallback;
        Debug.Log(s != null
            ? $"[Plate] Preview OK: '{debugPreviewKey}'"
            : $"[Plate] Clave '{debugPreviewKey}' no encontrada en plateSprites[].");
    }

    [ContextMenu("Log All Defined Keys")]
    private void EditorLogAllKeys()
    {
        if (plateSprites == null || plateSprites.Length == 0)
        {
            Debug.Log("[Plate] plateSprites[] está vacío.");
            return;
        }
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Plate] {plateSprites.Length} claves definidas:");
        foreach (var e in plateSprites)
            sb.AppendLine($"  '{e.key}' → {(e.sprite != null ? e.sprite.name : "NULL")}");
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Validate All 64 Keys")]
    private void EditorValidateAllKeys()
    {
        // Lista canónica completa de 64 claves + Empty
        string[] canonical = {
            "Empty",
            // A — Solo waffle
            "Perfect", "Overcooked", "Burned",
            // B — Waffle + 1
            "Perfect_Vanilla","Perfect_Strawberry","Perfect_Chocolate","Perfect_Honey",
            "Overcooked_Vanilla","Overcooked_Strawberry","Overcooked_Chocolate","Overcooked_Honey",
            "Burned_Vanilla","Burned_Strawberry","Burned_Chocolate","Burned_Honey",
            // C — Waffle + 2
            "Perfect_VanillaStrawberry","Perfect_VanillaChocolate","Perfect_VanillaHoney",
            "Perfect_StrawberryChocolate","Perfect_StrawberryHoney","Perfect_ChocolateHoney",
            "Overcooked_VanillaStrawberry","Overcooked_VanillaChocolate","Overcooked_VanillaHoney",
            "Overcooked_StrawberryChocolate","Overcooked_StrawberryHoney","Overcooked_ChocolateHoney",
            "Burned_VanillaStrawberry","Burned_VanillaChocolate","Burned_VanillaHoney",
            "Burned_StrawberryChocolate","Burned_StrawberryHoney","Burned_ChocolateHoney",
            // D — Waffle + 3
            "Perfect_VanillaStrawberryChocolate","Perfect_VanillaStrawberryHoney",
            "Perfect_VanillaChocolateHoney","Perfect_StrawberryChocolateHoney",
            "Overcooked_VanillaStrawberryChocolate","Overcooked_VanillaStrawberryHoney",
            "Overcooked_VanillaChocolateHoney","Overcooked_StrawberryChocolateHoney",
            "Burned_VanillaStrawberryChocolate","Burned_VanillaStrawberryHoney",
            "Burned_VanillaChocolateHoney","Burned_StrawberryChocolateHoney",
            // E — Waffle + todos
            "Perfect_VanillaStrawberryChocolateHoney",
            "Overcooked_VanillaStrawberryChocolateHoney",
            "Burned_VanillaStrawberryChocolateHoney",
            // F — Helados sin waffle
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
            // else OK — no loguear las 64 para no saturar
        }

        if (missing == 0 && nullSprite == 0)
            sb.AppendLine("  ✅ Todas las claves tienen sprite asignado.");
        else
            sb.AppendLine($"\n  Resumen: {missing} claves faltantes, {nullSprite} sprites null.");

        Debug.Log(sb.ToString());
    }
#endif
}