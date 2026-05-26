using UnityEngine;
using System.Collections;

/// <summary>
/// PLATAFORMA DE ENTREGA v2 — GDD sección 4.5
///
/// CAMBIOS v2:
///   - Evalúa WaffleCookState (Perfect/Overcooked/Burned) para recompensa y reacción del cliente.
///   - Penaliza paciencia de TODOS los clientes activos en entrega incorrecta.
///   - Llama Plate.ConsumeAndSpawnNew() SÓLO tras entrega correcta.
///   - NO destruye el Plate automáticamente si el pedido es incorrecto;
///     el Plate vuelve a la mesa por ReturnToOrigin() del DraggableItem.
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
    [Tooltip("Multiplicador de recompensa para waffle Perfect")]
    public float perfectMultiplier = 1.0f;
    [Tooltip("Multiplicador de recompensa para waffle Overcooked")]
    public float overcookedMultiplier = 0.6f;
    [Tooltip("Multiplicador de recompensa para waffle Burned (entrega válida si el cliente lo pidió así… raro, pero posible)")]
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
            OnCorrectDelivery(plate, deliveredRecipe, cookState);
        }
        else
        {
            OnIncorrectDelivery(item);
            // NO destruir el Plate — DraggableItem.HandleFailedDrop() lo devuelve
            // a la mesa porque destroyOnFailedDrop = false en el Plate.
        }
    }

    // ─────────────────────────────────────────────────────────────
    // RESULTADOS
    // ─────────────────────────────────────────────────────────────

    private void OnCorrectDelivery(Plate plate, RecipeType recipe, WaffleCookState cookState)
    {
        int baseReward = RecipeRewards.GetReward(recipe);

        // Modificar recompensa según calidad del waffle
        float multiplier = cookState switch
        {
            WaffleCookState.Overcooked => overcookedMultiplier,
            WaffleCookState.Burned => burnedMultiplier,
            _ => perfectMultiplier   // Perfect o sin waffle
        };

        int finalReward = Mathf.RoundToInt(baseReward * multiplier);
        gameManager?.AddMoney(finalReward);

        // Feedback según calidad
        if (cookState == WaffleCookState.Overcooked || cookState == WaffleCookState.Burned)
        {
            // Cliente disgustado — recibe el pedido pero no está feliz
            FeedbackManager.Instance?.ShowSuccessDelivery(transform.position, finalReward);
            AudioManager.Instance?.PlaySound(SoundType.DeliverySuccess);
            Debug.Log($"[DeliveryPlatform] ✅ Entregado (calidad baja: {cookState}) — ${finalReward}");
        }
        else
        {
            FeedbackManager.Instance?.ShowSuccessDelivery(transform.position, finalReward);
            AudioManager.Instance?.PlaySound(SoundType.DeliverySuccess);
            Debug.Log($"[DeliveryPlatform] ✅ Entregado perfectamente — ${finalReward}");
        }

        StartCoroutine(FlashColor(colorSuccess));

        // ÚNICO punto donde se genera un nuevo Plate
        plate.ConsumeAndSpawnNew();
    }

    private void OnIncorrectDelivery(DraggableItem item)
    {
        gameManager?.AddError();

        FeedbackManager.Instance?.ShowErrorDelivery(transform.position);
        AudioManager.Instance?.PlaySound(SoundType.DeliveryError);

        StartCoroutine(FlashColor(colorError));

        // El Plate vuelve a la mesa automáticamente — no lo destruimos aquí.
        // DraggableItem.TryDeliverToTarget → HandleFailedDrop → MoveToOriginCoroutine.
        Debug.Log("[DeliveryPlatform] ❌ Pedido incorrecto — el plato regresa a la mesa.");
    }

    // ─────────────────────────────────────────────────────────────
    // HIGHLIGHT
    // ─────────────────────────────────────────────────────────────

    void OnTriggerEnter2D(Collider2D other)
    {
        Plate plate = other.GetComponent<Plate>();
        DraggableItem dragging = other.GetComponent<DraggableItem>();
        if (dragging != null && plate != null && plate.HasRecipe)
            SetColor(colorHighlight);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        SetColor(colorDefault);
    }

    // ─────────────────────────────────────────────────────────────
    // VISUAL HELPERS
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

/// <summary>Recompensas base por receta — GDD sección 8.3</summary>
public static class RecipeRewards
{
    public static int GetReward(RecipeType recipe)
    {
        return recipe switch
        {
            RecipeType.WaffleSimple => 10,
            RecipeType.IceCreamAlone => 8,
            RecipeType.WaffleWithIceCream => 15,
            RecipeType.WaffleWithHoneyButter => 12,
            _ => 0
        };
    }
}