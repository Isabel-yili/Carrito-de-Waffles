using UnityEngine;
using System.Collections;

/// <summary>
/// HONEY SOURCE — fuente de miel con secuencia de animación de tres estados.
///
/// JERARQUÍA DEL PREFAB:
///   HoneySource          ← este script + Collider2D + Animator
///
/// ANIMATOR CONTROLLER requerido:
///   Estados:
///     IdleHoney           ← estado de reposo (loop)
///     OpenHoney           ← animación de abrir el tarro
///     CloseHoney          ← animación de cerrar el tarro
///
///   Parámetros:
///     Trigger  "DoOpen"   ← dispara OpenHoney desde Idle
///     Trigger  "DoClose"  ← dispara CloseHoney desde OpenHoney
///     Trigger  "DoIdle"   ← vuelve a IdleHoney desde Close
///
///   Transiciones recomendadas:
///     IdleHoney   → OpenHoney   (DoOpen,   Has Exit Time = false)
///     OpenHoney   → CloseHoney  (DoClose,  Has Exit Time = false)
///     CloseHoney  → IdleHoney   (DoIdle,   Has Exit Time = false)
///
/// FLUJO COMPLETO:
///   1. Click → Trigger "DoOpen" → animación OpenHoney
///   2. Fin de openDuration → spawn del HoneyItem en cursor
///   3. DragManager.OnItemPickedUp() → jugador lleva la miel
///   4. Trigger "DoClose" → animación CloseHoney
///   5. Fin de closeDuration → Trigger "DoIdle" → reposo
///
///   El item (HoneyItem):
///     • persistentDrag      = false   → cursor-follow
///     • destroyOnFailedDrop = true    → se destruye si el drop falla
///     • Solo interactúa con Plate (Plate.CanReceive filtra HoneyButter)
///
/// SETUP:
///   - honeyAnimator: el Animator en el mismo GameObject (o hijo AnimatedLayer).
///   - honeyItemPrefab: prefab con DraggableItem (itemType = HoneyButter).
///   - openDuration / closeDuration: duración real de cada animación Procreate.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class HoneySource : MonoBehaviour
{
    // ─── Inspector ─────────────────────────────────────────────────

    [Header("══ Animator ══")]
    [Tooltip("Animator del tarro de miel (en este mismo GameObject o en un hijo AnimatedLayer)")]
    public Animator honeyAnimator;

    [Tooltip("Duración de la animación OpenHoney en segundos. " +
             "El item aparece al terminar esta animación.")]
    public float openDuration = 0.35f;

    [Tooltip("Duración de la animación CloseHoney en segundos.")]
    public float closeDuration = 0.3f;

    [Header("══ Parámetros del Animator ══")]
    public string triggerOpen = "DoOpen";
    public string triggerClose = "DoClose";
    public string triggerIdle = "DoIdle";

    [Header("══ Prefab de miel ══")]
    [Tooltip("Prefab del ítem cursor-follow (DraggableItem, itemType = HoneyButter)")]
    public GameObject honeyItemPrefab;

    [Header("══ FX ══")]
    [Tooltip("Partículas opcionales al verter la miel")]
    public ParticleSystem pourParticle;

    // ─── Estado interno ─────────────────────────────────────────────
    private bool _isPouring = false;

    // ═══════════════════════════════════════════════════════════════
    // INPUT
    // ═══════════════════════════════════════════════════════════════

    void OnMouseDown()
    {
        DragManager.Instance?.MarkClickHandled();

        // Input lock: no durante animación
        if (_isPouring) return;

        // No iniciar si el jugador ya lleva algo
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem) return;

        StartCoroutine(PourSequence());
    }

    // ═══════════════════════════════════════════════════════════════
    // SECUENCIA: Open → Spawn → Close → Idle
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator PourSequence()
    {
        _isPouring = true;

        // ── 1. Abrir el tarro ────────────────────────────────────
        SetTrigger(triggerOpen);
        Debug.Log($"[HoneySource] Animación '{triggerOpen}'. Esperando {openDuration}s…");
        yield return new WaitForSeconds(openDuration);

        // ── 2. Spawnear el item en el cursor ─────────────────────
        if (honeyItemPrefab == null)
        {
            Debug.LogError("[HoneySource] honeyItemPrefab no asignado.");
            CloseAndIdle();
            _isPouring = false;
            yield break;
        }

        Vector3 spawnPos = GetCursorWorldPos();
        GameObject go = Instantiate(honeyItemPrefab, spawnPos, Quaternion.identity);

        DraggableItem item = go.GetComponent<DraggableItem>();
        if (item == null)
        {
            Debug.LogError("[HoneySource] honeyItemPrefab no tiene DraggableItem.");
            Destroy(go);
            CloseAndIdle();
            _isPouring = false;
            yield break;
        }

        // Modo A — cursor-follow desechable
        item.itemType = ItemType.HoneyButter;
        item.persistentDrag = false;
        item.destroyOnFailedDrop = true;

        DragManager.Instance?.OnItemPickedUp(item);
        AudioManager.Instance?.PlaySound(SoundType.ItemPickup);

        if (pourParticle != null)
            pourParticle.Play();

        Debug.Log("[HoneySource] HoneyItem spawneado y entregado al cursor.");

        // ── 3. Cerrar el tarro ───────────────────────────────────
        SetTrigger(triggerClose);
        yield return new WaitForSeconds(closeDuration);

        // ── 4. Volver al idle ────────────────────────────────────
        SetTrigger(triggerIdle);
        _isPouring = false;

        Debug.Log("[HoneySource] Secuencia completa → IdleHoney.");
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Cierra y vuelve al idle sin spawnear item (ruta de error).
    /// </summary>
    private void CloseAndIdle()
    {
        SetTrigger(triggerClose);
        StartCoroutine(DelayedTrigger(triggerIdle, closeDuration));
    }

    private IEnumerator DelayedTrigger(string trigger, float delay)
    {
        yield return new WaitForSeconds(delay);
        SetTrigger(trigger);
    }

    private void SetTrigger(string trigger)
    {
        if (honeyAnimator != null && !string.IsNullOrEmpty(trigger))
            honeyAnimator.SetTrigger(trigger);
    }

    private Vector3 GetCursorWorldPos()
    {
        Camera cam = Camera.main;
        if (cam == null) return transform.position;
        Vector3 p = cam.ScreenToWorldPoint(Input.mousePosition);
        p.z = 0f;
        return p;
    }
}