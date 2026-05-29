using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// FUENTE DE ÍTEMS v5 — Fix definitivo de animación en helados.
///
/// CAUSA RAÍZ:
///   SetActive(false) sobre visualLayer desactiva el Animator completamente.
///   Cada click hace SetActive(true) → el Animator reinicia desde el estado
///   inicial → el trigger se consume en la pose 0 → SetActive(false) antes
///   de que Unity renderice un solo frame animado. La animación nunca es visible.
///
/// SOLUCIÓN:
///   El visualLayer NUNCA se desactiva con SetActive().
///   La visibilidad se controla exclusivamente con SpriteRenderer.enabled.
///   El Animator permanece activo y en estado correcto en todo momento.
///
///   Al inicio (Awake):   todos los SpriteRenderers del visualLayer → enabled = false
///   Al hacer click:      SpriteRenderers → enabled = true, SetTrigger()
///   Al terminar spawn:   SpriteRenderers → enabled = false
///
///   El Animator corre continuamente — no se reinicia entre clicks.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemSource : MonoBehaviour, IItemSource, IItemReceiver
{
    // ─── Inspector ──────────────────────────────────────────────────

    [Header("══ Configuración ══")]
    public ItemType producedItemType;

    [Tooltip("Prefab del ítem. Debe contener SpriteRenderer + Collider2D + DraggableItem.\n" +
             "El SpriteRenderer debe tener el sprite del sabor correcto, no el del contenedor.")]
    public GameObject itemPrefab;

    [Header("══ Visual de la fuente ══")]
    [Tooltip("El hijo que contiene el SpriteRenderer de la cuchara y el Animator.\n" +
             "IMPORTANTE: este GameObject se mantiene SIEMPRE activo (SetActive nunca se llama).\n" +
             "La visibilidad se controla solo a través de SpriteRenderer.enabled.")]
    public GameObject visualLayer;

    [Header("══ Animación ══")]
    [Tooltip("Animator dentro de visualLayer. Debe estar en un GameObject siempre activo.")]
    public Animator sourceAnimator;

    [Tooltip("Nombre del Trigger en el Animator.")]
    public string selectTrigger = "IceCreamSelect";

    [Tooltip("Duración total de la animación en segundos.")]
    public float animationDuration = 0.3f;

    [Tooltip("TRUE  = ítem aparece al inicio de la animación (más responsivo).\n" +
             "FALSE = ítem aparece al terminar la animación.")]
    public bool spawnDuringAnimation = true;

    [Header("══ FX ══")]
    public ParticleSystem selectParticle;

    // ─── Estado interno ─────────────────────────────────────────────
    private bool _isSpawning = false;

    public ItemType ProducedItemType => producedItemType;

    // ═══════════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {

    }

    // ═══════════════════════════════════════════════════════════════
    // IItemSource
    // ═══════════════════════════════════════════════════════════════

    public DraggableItem SpawnItem()
    {
        if (itemPrefab == null)
        {
            Debug.LogError($"[ItemSource] '{gameObject.name}': itemPrefab no asignado para {producedItemType}.");
            return null;
        }

        GameObject go = Instantiate(itemPrefab, GetCursorWorldPos(), Quaternion.identity);
        DraggableItem item = go.GetComponent<DraggableItem>();
        item.ownerMixAnimator = GetComponent<WaffleMixAnimatorSync>();

        if (item == null)
        {
            Debug.LogError($"[ItemSource] El prefab '{itemPrefab.name}' no tiene DraggableItem.");
            Destroy(go);
            return null;
        }

        item.itemType = producedItemType;
        item.persistentDrag = false;
        item.destroyOnFailedDrop = true;

        return item;
    }

    // IItemReceiver — las fuentes no aceptan ítems
    public bool CanReceive(DraggableItem item) => false;
    public void ReceiveItem(DraggableItem item) { }

    // ═══════════════════════════════════════════════════════════════
    // INPUT
    // ═══════════════════════════════════════════════════════════════

    void OnMouseDown()
    {
        DragManager.Instance?.MarkClickHandled();

        if (_isSpawning) return;
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem) return;

        StartCoroutine(SelectSequence());
    }

    // ═══════════════════════════════════════════════════════════════
    // SECUENCIA
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator SelectSequence()
    {
        _isSpawning = true;

        GetComponent<WaffleMixAnimatorSync>()?.NotifySelected();

        if (selectParticle != null)
            selectParticle.Play();

        if (sourceAnimator != null && sourceAnimator.isActiveAndEnabled)
            sourceAnimator.SetTrigger(selectTrigger);

        DraggableItem spawned = null;

        if (spawnDuringAnimation)
        {
            spawned = SpawnItem();
            yield return new WaitForSeconds(animationDuration);
        }
        else
        {
            yield return new WaitForSeconds(animationDuration);
            spawned = SpawnItem();
        }


        if (spawned != null)
        {
            Debug.Log($"[ItemSource] '{gameObject.name}' → '{spawned.name}' spawneado.");
            DragManager.Instance?.OnItemPickedUp(spawned);
        }
        else
        {
            Debug.LogWarning($"[ItemSource] '{gameObject.name}' → SpawnItem() devolvió null.");
        }

        _isSpawning = false;
    }

    // ═══════════════════════════════════════════════════════════════
    // CONTROL DE VISIBILIDAD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Activa o desactiva los SpriteRenderers del visualLayer sin tocar SetActive().
    /// De esta forma el Animator nunca se reinicia entre clicks.
    /// </summary>

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private Vector3 GetCursorWorldPos()
    {
        Camera cam = Camera.main;
        if (cam == null) return transform.position;
        Vector3 p = cam.ScreenToWorldPoint(Input.mousePosition);
        p.z = 0f;
        return p;
    }
}