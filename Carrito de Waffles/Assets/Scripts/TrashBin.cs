using UnityEngine;
using System.Collections;

/// <summary>
/// CUBO DE BASURA — GDD sección 4.4
/// "Arrastrar al cubo de basura (o clic directo)"
/// Acepta cualquier ítem y lo destruye con animación.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TrashBin : MonoBehaviour, IItemReceiver
{
    [Header("Visual")]
    public Animator trashAnimator;
    public SpriteRenderer lidSprite;

    public bool CanReceive(DraggableItem item)
    {
        // El cubo acepta cualquier ítem (quemados, errores, lo que sea)
        return item != null;
    }

    public void ReceiveItem(DraggableItem item)
    {
        if (item == null) return;

        AudioManager.Instance?.PlaySound(SoundType.ItemPlaced); // Sonido de cubo metálico
        StartCoroutine(TrashAnimation(item));
    }

    private IEnumerator TrashAnimation(DraggableItem item)
    {
        // Animación: ítem "cae" hacia el cubo
        Vector3 startPos = item.transform.position;
        float elapsed = 0f;
        float duration = 0.2f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            item.transform.position = Vector3.Lerp(startPos, transform.position, t);
            item.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, t);
            yield return null;
        }

        Destroy(item.gameObject);

        // Abrir y cerrar la tapa
        if (trashAnimator != null)
            trashAnimator.SetTrigger("Open");
    }

    // Highlight al arrastrar encima
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<DraggableItem>() != null)
            transform.localScale = Vector3.one * 1.1f;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        transform.localScale = Vector3.one;
    }
}
