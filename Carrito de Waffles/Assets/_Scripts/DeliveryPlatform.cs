using UnityEngine;
using System.Collections;

/// <summary>
/// PLATAFORMA DE ENTREGA v3 — GDD sección 4.5
///
/// FIX CRÍTICO v3:
///   El flujo de entrega exitosa ahora sigue el orden correcto obligatorio:
///
///     1. Evaluar pedido (OrderManager)
///     2. Marcar el plate como consumido (bloquear más drops sobre él)
///     3. Limpiar DragManager (OnItemReleased) — ANTES de destruir el objeto
///     4. Calcular recompensa y aplicarla
///     5. Feedback visual/sonoro
///     6. Plate.ConsumeAndSpawnNew() → Destroy(oldPlate) + Instantiate(newPlate)
///
///   El paso 3 es el que estaba ausente en v2, causando que el DragManager
///   quedara bloqueado con _hasSelectedItem = true tras la entrega.
///
///   ENTREGA INCORRECTA:
///   - GameManager.AddError()
///   - El Plate vuelve a la mesa automáticamente porque:
///       DraggableItem.HandleFailedDrop() → persistentDrag=true → queda donde se suelta
///       o MoveToOriginCoroutine() si tiene _originOven == null.
///   - NO destruimos ni movemos el plate aquí — DraggableItem ya lo maneja.
/// </summary>
public class DeliveryPlatform : MonoBehaviour, IItemReceiver
{
    [Header("Visual Feedback")]
    public SpriteRenderer platformSprite;
    public Color colorDefault = new Color(1f, 1f, 1f, 0.5f);
    public Color colorHighlight = new Color(0.3f, 1f, 0.3f, 0.8f);
    public Color colorSuccess = new Color(0.2f, 0.9f, 0.2f, 1f);
    public Color colorError = new Color(0.9f, 0.2f, 0.2f, 1f);

    [Header("Recompensas por calidad (GDD 8.3)")]
    public float perfectMultiplier = 1.0f;
    public float overcookedMultiplier = 0.6f;
    public float burnedMultiplier = 0.2f;

    [Header("Referencias")]
    public OrderManager orderManager;
    public GameManager gameManager;

    // ─────────────────────────────────────────────────────────────
    // IItemReceiver
    // ─────────────────────────────────────────────────────────────

    public bool CanReceive(DraggableItem item)
    {
        Plate plate = item.GetComponent<Plate>();
        return plate != null && plate.HasRecipe;
    }

    public void ReceiveItem(DraggableItem item)
    {
        Plate plate = item.GetComponent<Plate>();
        if (plate == null || !plate.HasRecipe) return;

        RecipeType deliveredRecipe = plate.CompletedRecipe.Value;
        WaffleCookState cookState = plate.Recipe.cookState;

        bool success = orderManager != null && orderManager.TryFulfillOrder(deliveredRecipe);

        if (success)
        {
            OnCorrectDelivery(item, plate, deliveredRecipe, cookState);
        }
        else
        {
            OnIncorrectDelivery(item);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // ENTREGA CORRECTA — orden de operaciones es crítico
    // ─────────────────────────────────────────────────────────────

    private void OnCorrectDelivery(DraggableItem item, Plate plate,
                                   RecipeType recipe, WaffleCookState cookState)
    {
        // ── 1. Calcular recompensa ────────────────────────────────
        int baseReward = RecipeRewards.GetReward(recipe);
        float multiplier = cookState switch
        {
            WaffleCookState.Overcooked => overcookedMultiplier,
            WaffleCookState.Burned => burnedMultiplier,
            _ => perfectMultiplier
        };
        int finalReward = Mathf.RoundToInt(baseReward * multiplier);

        // ── 2. Limpiar DragManager ANTES de destruir el objeto ────
        // Si no hacemos esto, DragManager._hasSelectedItem queda true
        // para siempre y el juego se traba.
        // OnItemReleased llama item.StopCarrying() internamente.
        DragManager.Instance?.OnItemReleased(item);

        // ── 3. Aplicar dinero ─────────────────────────────────────
        gameManager?.AddMoney(finalReward);

        // ── 4. Feedback ───────────────────────────────────────────
        FeedbackManager.Instance?.ShowSuccessDelivery(transform.position, finalReward);
        AudioManager.Instance?.PlaySound(SoundType.DeliverySuccess);
        StartCoroutine(FlashColor(colorSuccess));

        string qualityLog = cookState == WaffleCookState.Perfect
            ? "perfectamente"
            : $"calidad baja ({cookState})";
        Debug.Log($"[DeliveryPlatform] ✅ Entregado {qualityLog} — ${finalReward}");

        // ── 5. Consumir plate e instanciar uno nuevo ──────────────
        // ConsumeAndSpawnNew() llama Destroy(gameObject) internamente.
        // DEBE ser lo último porque destruye el objeto al que apunta "item".
        plate.ConsumeAndSpawnNew();
    }

    // ─────────────────────────────────────────────────────────────
    // ENTREGA INCORRECTA
    // ─────────────────────────────────────────────────────────────

    private void OnIncorrectDelivery(DraggableItem item)
    {
        gameManager?.AddError();

        FeedbackManager.Instance?.ShowErrorDelivery(transform.position);
        AudioManager.Instance?.PlaySound(SoundType.DeliveryError);
        StartCoroutine(FlashColor(colorError));

        // NO llamamos OnItemReleased aquí.
        // DraggableItem.TryDeliverToTarget() ya llamó OnItemReleased()
        // después de que ReceiveItem() devolvió el control.
        // El plate vuelve a la mesa por HandleFailedDrop().

        Debug.Log("[DeliveryPlatform] ❌ Pedido incorrecto — el plate regresa a la mesa.");
    }

    // ─────────────────────────────────────────────────────────────
    // HIGHLIGHT — feedback visual al arrastrar el plate encima
    // ─────────────────────────────────────────────────────────────

    void OnTriggerEnter2D(Collider2D other)
    {
        Plate plate = other.GetComponent<Plate>();
        if (plate != null && plate.HasRecipe)
            SetColor(colorHighlight);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        SetColor(colorDefault);
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private IEnumerator FlashColor(Color flashColor)
    {
        SetColor(flashColor);
        yield return new WaitForSeconds(0.4f);
        SetColor(colorDefault);
    }

    private void SetColor(Color c)
    {
        if (platformSprite != null)
            platformSprite.color = c;
    }
}

// ─────────────────────────────────────────────────────────────────
// RECOMPENSAS — GDD sección 8.3
// ─────────────────────────────────────────────────────────────────

/// <summary>Recompensas base por receta.</summary>
public static class RecipeRewards
{
    public static int GetReward(RecipeType recipe) => recipe switch
    {
        RecipeType.WaffleSimple => 10,
        RecipeType.IceCreamAlone => 8,
        RecipeType.WaffleWithIceCream => 15,
        RecipeType.WaffleWithHoneyButter => 12,
        _ => 0
    };
}