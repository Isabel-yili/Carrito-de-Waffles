using UnityEngine;
using System.Collections;

/// <summary>
/// PLATAFORMA DE ENTREGA — GDD sección 4.5
/// "El jugador arrastra el plato terminado a la plataforma de entrega.
///  El sistema compara el contenido del plato con los pedidos activos en cola."
/// </summary>
public class DeliveryPlatform : MonoBehaviour, IItemReceiver
{
    [Header("Visual Feedback")]
    public SpriteRenderer platformSprite;
    public Color colorDefault  = new Color(1f, 1f, 1f, 0.5f);
    public Color colorHighlight = new Color(0.3f, 1f, 0.3f, 0.8f);
    public Color colorSuccess  = new Color(0.2f, 0.9f, 0.2f, 1f);
    public Color colorError    = new Color(0.9f, 0.2f, 0.2f, 1f);

    [Header("Referencias")]
    public OrderManager orderManager;
    public GameManager gameManager;

    // ─────────────────────────────────────────────────────────────
    // IItemReceiver
    // ─────────────────────────────────────────────────────────────

    public bool CanReceive(DraggableItem item)
    {
        // Solo acepta platos con receta completa
        Plate plate = item.GetComponent<Plate>();
        return plate != null && plate.HasRecipe;
    }

    public void ReceiveItem(DraggableItem item)
    {
        Plate plate = item.GetComponent<Plate>();
        if (plate == null || !plate.HasRecipe) return;

        RecipeType deliveredRecipe = plate.CompletedRecipe.Value;

        // Evaluar contra pedidos activos
        bool success = orderManager != null && orderManager.TryFulfillOrder(deliveredRecipe);

        if (success)
        {
            OnCorrectDelivery(deliveredRecipe);
        }
        else
        {
            OnIncorrectDelivery();
        }

        // Destruir el plato (entregado o rechazado)
        Destroy(item.gameObject);
    }

    // ─────────────────────────────────────────────────────────────
    // RESULTADOS
    // ─────────────────────────────────────────────────────────────

    private void OnCorrectDelivery(RecipeType recipe)
    {
        int reward = RecipeRewards.GetReward(recipe);
        gameManager?.AddMoney(reward);

        // GDD: "destello verde + texto flotante '+$XX' + animación de cliente feliz"
        FeedbackManager.Instance?.ShowSuccessDelivery(transform.position, reward);
        AudioManager.Instance?.PlaySound(SoundType.DeliverySuccess);

        StartCoroutine(FlashColor(colorSuccess));
    }

    private void OnIncorrectDelivery()
    {
        gameManager?.AddError();

        // GDD: "destello rojo + texto flotante 'ERROR' + reducción del contador"
        FeedbackManager.Instance?.ShowErrorDelivery(transform.position);
        AudioManager.Instance?.PlaySound(SoundType.DeliveryError);

        StartCoroutine(FlashColor(colorError));
    }

    // ─────────────────────────────────────────────────────────────
    // HIGHLIGHT (cuando el jugador arrastra un plato)
    // ─────────────────────────────────────────────────────────────

    void OnTriggerEnter2D(Collider2D other)
    {
        DraggableItem dragging = other.GetComponent<DraggableItem>();
        Plate plate = other.GetComponent<Plate>();
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

/// <summary>Recompensas por receta — GDD sección 8.3</summary>
public static class RecipeRewards
{
    public static int GetReward(RecipeType recipe)
    {
        return recipe switch
        {
            RecipeType.WaffleSimple          => 10,
            RecipeType.IceCreamAlone         => 8,
            RecipeType.WaffleWithIceCream    => 15,
            RecipeType.WaffleWithHoneyButter => 12,
            _                                => 0
        };
    }
}
