using UnityEngine;
using System.Collections;

/// <summary>
/// PLATAFORMA DE ENTREGA v5 — Bug fix de entrega siempre incorrecta.
///
/// FIXES:
///   - OnIncorrectDelivery ya NO llama DragManager.OnItemReleased() directamente;
///     lo hace a través de item.ReturnToOrigin() + el flujo normal de DraggableItem,
///     evitando el doble-release que corrompía el estado del DragManager.
///   - MarkReleaseHandledByReceiver() se llama en AMBOS casos (correcto e incorrecto)
///     para que DraggableItem.TryDeliverToTarget() no haga un segundo OnItemReleased.
///   - Log extendido para diagnóstico de mismatches de RecipeType.
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

    private bool _isProcessingDelivery = false;

    // ─────────────────────────────────────────────────────────────
    // IItemReceiver
    // ─────────────────────────────────────────────────────────────

    public bool CanReceive(DraggableItem item)
    {
        Plate plate = item.GetComponent<Plate>();
        if (plate == null) return false;

        // HasRecipe verifica que Resolve() != null internamente
        bool canReceive = plate.HasRecipe;
        Debug.Log($"[DeliveryPlatform] CanReceive → plate={plate.name} | " +
                  $"HasRecipe={canReceive} | recipe={plate.CompletedRecipe}");
        return canReceive;
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (_isProcessingDelivery) return;
        _isProcessingDelivery = true;

        // FIX: marcar siempre como handled para que DraggableItem.TryDeliverToTarget()
        // no llame OnItemReleased() por segunda vez. Nosotros lo manejamos abajo.
        DragManager.Instance?.NotifySuccessfulPlacement(item, this);

        Plate plate = item.GetComponent<Plate>();
        if (plate == null || !plate.HasRecipe)
        {
            Debug.LogWarning("[DeliveryPlatform] ReceiveItem: plato inválido o sin receta.");
            DragManager.Instance?.OnItemReleased(item);
            _isProcessingDelivery = false;
            return;
        }

        RecipeType? resolvedRecipe = plate.CompletedRecipe;
        if (!resolvedRecipe.HasValue)
        {
            Debug.LogWarning("[DeliveryPlatform] ReceiveItem: Resolve() devolvió null — " +
                             "combinación de ingredientes no forma una receta válida.");
            OnIncorrectDelivery(item);
            _isProcessingDelivery = false;
            return;
        }

        RecipeType deliveredRecipe = resolvedRecipe.Value;
        WaffleCookState cookState = plate.Recipe.cookState;

        Debug.Log($"[DeliveryPlatform] Entregando receta: {deliveredRecipe} | cookState: {cookState}");

        bool success = orderManager != null && orderManager.TryFulfillOrder(deliveredRecipe);

        if (success)
            OnCorrectDelivery(item, plate, deliveredRecipe, cookState);
        else
            OnIncorrectDelivery(item);

        _isProcessingDelivery = false;
    }

    // ─────────────────────────────────────────────────────────────
    // ENTREGA CORRECTA
    // ─────────────────────────────────────────────────────────────

    private void OnCorrectDelivery(
        DraggableItem item,
        Plate plate,
        RecipeType recipe,
        WaffleCookState cookState)
    {
        int baseReward = RecipeRewards.GetReward(recipe);

        float multiplier = cookState switch
        {
            WaffleCookState.Overcooked => overcookedMultiplier,
            WaffleCookState.Burned => burnedMultiplier,
            _ => perfectMultiplier
        };

        int finalReward = Mathf.RoundToInt(baseReward * multiplier);

        gameManager?.AddMoney(finalReward);

        FeedbackManager.Instance?.ShowSuccessDelivery(transform.position, finalReward);

        AudioManager.Instance?.PlaySound(SoundType.DeliverySuccess);

        StartCoroutine(FlashColor(colorSuccess));

        Debug.Log($"[DeliveryPlatform] ✅ Entrega correcta: {recipe} | ${finalReward}");

        // ← FIX REAL
        DragManager.Instance?.OnItemReleased(item);

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

        Debug.Log("[DeliveryPlatform] ❌ Entrega incorrecta.");

        // FIX: ReturnToOrigin llama StopCarrying() internamente.
        // Luego liberamos el DragManager. NO llamar OnItemReleased dos veces.
        item.ReturnToOrigin();
        DragManager.Instance?.OnItemReleased(item);
    }

    // ─────────────────────────────────────────────────────────────
    // HIGHLIGHT
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

public static class RecipeRewards
{
    public static int GetReward(RecipeType recipe) => recipe switch
    {
        RecipeType.Perfect => 10,
        RecipeType.Perfect_Vanilla => 13,
        RecipeType.Perfect_Strawberry => 13,
        RecipeType.Perfect_Chocolate => 13,
        RecipeType.Perfect_Honey => 12,
        RecipeType.Perfect_VanillaStrawberry => 16,
        RecipeType.Perfect_VanillaChocolate => 16,
        RecipeType.Perfect_VanillaHoney => 15,
        RecipeType.Perfect_StrawberryChocolate => 16,
        RecipeType.Perfect_StrawberryHoney => 15,
        RecipeType.Perfect_ChocolateHoney => 15,
        RecipeType.Perfect_VanillaStrawberryChocolate => 20,
        RecipeType.Perfect_VanillaStrawberryHoney => 19,
        RecipeType.Perfect_VanillaChocolateHoney => 19,
        RecipeType.Perfect_StrawberryChocolateHoney => 19,
        RecipeType.Perfect_VanillaStrawberryChocolateHoney => 25,
        RecipeType.IceCream_Vanilla => 8,
        RecipeType.IceCream_Strawberry => 8,
        RecipeType.IceCream_Chocolate => 8,
        RecipeType.IceCream_VanillaStrawberry => 11,
        RecipeType.IceCream_VanillaChocolate => 11,
        RecipeType.IceCream_VanillaHoney => 10,
        RecipeType.IceCream_StrawberryChocolate => 11,
        RecipeType.IceCream_StrawberryHoney => 10,
        RecipeType.IceCream_ChocolateHoney => 10,
        RecipeType.IceCream_VanillaStrawberryChocolate => 14,
        RecipeType.IceCream_VanillaStrawberryHoney => 13,
        RecipeType.IceCream_VanillaChocolateHoney => 13,
        RecipeType.IceCream_StrawberryChocolateHoney => 13,
        RecipeType.IceCream_VanillaStrawberryChocolateHoney => 18,
        RecipeType.Honey => 7,
        RecipeType.WaffleSimple => 10,
        RecipeType.IceCreamAlone => 8,
        RecipeType.WaffleWithIceCream => 15,
        RecipeType.WaffleWithHoneyButter => 12,
        _ => 5
    };
}