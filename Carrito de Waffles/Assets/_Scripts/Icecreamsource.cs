using UnityEngine;
using System.Collections;

/// <summary>
/// ICE CREAM SOURCE — fuente de helados con tres sabores y Animator compartido.
///
/// JERARQUÍA DEL PREFAB:
///   IceCreamSource               ← este script + Animator
///   ├── VanillaZone              ← GameObject con Collider2D + IceCreamFlavor (Vanilla)
///   ├── StrawberryZone           ← GameObject con Collider2D + IceCreamFlavor (Strawberry)
///   └── ChocolateZone            ← GameObject con Collider2D + IceCreamFlavor (Chocolate)
///
/// ANIMATOR CONTROLLER requerido (en el mismo GameObject raíz):
///   Estados:    IdleIceCream | VanillaIceCream | StrawberryIceCream | ChocolateIceCream
///   Parámetros: Trigger "VanillaIceCream" | "StrawberryIceCream" | "ChocolateIceCream"
///   Transiciones: cualquier estado → el estado del sabor clicado → IdleIceCream (automático)
///
/// FLUJO:
///   1. El jugador hace click en una de las zonas (VanillaZone / StrawberryZone / ChocolateZone).
///   2. IceCreamFlavor.OnMouseDown() notifica a este script vía RequestScoop(flavor).
///   3. Se activa el Trigger del Animator correspondiente.
///   4. Se espera animationDuration segundos (lock de input activo).
///   5. Se instancia el prefab del sabor en la posición del cursor.
///   6. DragManager.OnItemPickedUp() lo registra → el jugador lo lleva al Plate.
///   7. Si falla el drop → destroyOnFailedDrop = true → se destruye inmediatamente.
///
/// SETUP:
///   - Asignar scoop animator en Inspector.
///   - Asignar los tres prefabs (vanillaPrefab, strawberryPrefab, chocolatePrefab).
///   - En cada hijo *Zone, añadir componente IceCreamFlavor y seleccionar el sabor.
///   - animationDuration debe coincidir con la duración real de la animación Procreate.
/// </summary>
public class IceCreamSource : MonoBehaviour
{
    // ─── Inspector ─────────────────────────────────────────────────

    [Header("══ Animator ══")]
    [Tooltip("Animator en el raíz de IceCreamSource")]
    public Animator scoopAnimator;

    [Tooltip("Duración de la animación de sacar helado (segundos). " +
             "Debe coincidir con la duración real de la animación Procreate.")]
    public float animationDuration = 0.4f;

    [Header("══ Prefabs de helado ══")]
    [Tooltip("Prefab del ítem cursor-follow para vainilla (DraggableItem con itemType=IceCreamVanilla)")]
    public GameObject vanillaPrefab;
    [Tooltip("Prefab del ítem cursor-follow para fresa")]
    public GameObject strawberryPrefab;
    [Tooltip("Prefab del ítem cursor-follow para chocolate")]
    public GameObject chocolatePrefab;

    [Header("══ Parámetros del Animator ══")]
    [Tooltip("Trigger para la animación de vainilla")]
    public string triggerVanilla = "VanillaIceCream";
    [Tooltip("Trigger para la animación de fresa")]
    public string triggerStrawberry = "StrawberryIceCream";
    [Tooltip("Trigger para la animación de chocolate")]
    public string triggerChocolate = "ChocolateIceCream";

    [Header("══ FX ══")]
    [Tooltip("Partículas opcionales al sacar helado (frío, chispas, etc.)")]
    public ParticleSystem scoopParticle;

    // ─── Estado interno ─────────────────────────────────────────────
    private bool _isScooping = false;

    public bool IsScooping => _isScooping;

    // ═══════════════════════════════════════════════════════════════
    // API PÚBLICA — llamada por IceCreamFlavor.OnMouseDown()
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Solicita sacar helado del sabor indicado.
    /// Llamado desde IceCreamFlavor (componente en cada zona hija).
    /// Si ya hay una animación en curso (_isScooping), la solicitud se ignora.
    /// </summary>
    public void RequestScoop(IceCreamFlavor.Flavor flavor)
    {
        // Input lock: evitar spam
        if (_isScooping) return;

        // No iniciar si el jugador ya lleva algo
        if (DragManager.Instance != null && DragManager.Instance.HasSelectedItem) return;

        StartCoroutine(ScoopSequence(flavor));
    }

    // ═══════════════════════════════════════════════════════════════
    // SECUENCIA DE ANIMACIÓN → SPAWN
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator ScoopSequence(IceCreamFlavor.Flavor flavor)
    {
        _isScooping = true;

        // 1. Lanzar animación
        string trigger = flavor switch
        {
            IceCreamFlavor.Flavor.Vanilla => triggerVanilla,
            IceCreamFlavor.Flavor.Strawberry => triggerStrawberry,
            IceCreamFlavor.Flavor.Chocolate => triggerChocolate,
            _ => triggerVanilla
        };

        if (scoopAnimator != null)
            scoopAnimator.SetTrigger(trigger);

        if (scoopParticle != null)
            scoopParticle.Play();

        Debug.Log($"[IceCreamSource] Animación '{trigger}' iniciada. Esperando {animationDuration}s…");

        // 2. Esperar a que termine la animación — el item aparece AL FINAL
        yield return new WaitForSeconds(animationDuration);

        // 3. Instanciar el prefab correcto
        GameObject prefab = flavor switch
        {
            IceCreamFlavor.Flavor.Vanilla => vanillaPrefab,
            IceCreamFlavor.Flavor.Strawberry => strawberryPrefab,
            IceCreamFlavor.Flavor.Chocolate => chocolatePrefab,
            _ => vanillaPrefab
        };

        ItemType itemType = flavor switch
        {
            IceCreamFlavor.Flavor.Vanilla => ItemType.IceCreamVanilla,
            IceCreamFlavor.Flavor.Strawberry => ItemType.IceCreamStrawberry,
            IceCreamFlavor.Flavor.Chocolate => ItemType.IceCreamChocolate,
            _ => ItemType.IceCreamVanilla
        };

        if (prefab == null)
        {
            Debug.LogError($"[IceCreamSource] Prefab no asignado para {flavor}.");
            _isScooping = false;
            yield break;
        }

        // Instanciar en posición del cursor
        Vector3 spawnPos = GetCursorWorldPos();
        GameObject go = Instantiate(prefab, spawnPos, Quaternion.identity);

        DraggableItem item = go.GetComponent<DraggableItem>();
        if (item == null)
        {
            Debug.LogError($"[IceCreamSource] El prefab de {flavor} no tiene DraggableItem.");
            Destroy(go);
            _isScooping = false;
            yield break;
        }

        // Configurar como Modo A — cursor-follow desechable
        item.itemType = itemType;
        item.persistentDrag = false;
        item.destroyOnFailedDrop = true;

        // 4. Entregar al DragManager → el jugador lo lleva
        DragManager.Instance?.OnItemPickedUp(item);

        AudioManager.Instance?.PlaySound(SoundType.ItemPickup);
        Debug.Log($"[IceCreamSource] {flavor} spawneado y entregado al cursor.");

        _isScooping = false;
    }

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

// ═══════════════════════════════════════════════════════════════════
// ICE CREAM FLAVOR — componente en cada zona hija (Collider2D)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Componente ligero que vive en cada zona hija de IceCreamSource.
/// Solo tiene Collider2D y este script.
/// Su único trabajo: detectar el click y notificar al padre.
///
/// SETUP:
///   1. Crear hijos VanillaZone, StrawberryZone, ChocolateZone bajo IceCreamSource.
///   2. Añadir Collider2D (BoxCollider2D o CircleCollider2D, isTrigger = false).
///   3. Añadir este script y seleccionar el Flavor correcto.
///   4. NO necesita Rigidbody2D — Unity detecta OnMouseDown sin él en 2D
///      si la cámara tiene el tag "MainCamera" y el objeto tiene Collider2D.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class IceCreamFlavor : MonoBehaviour
{
    public enum Flavor { Vanilla, Strawberry, Chocolate }

    [Tooltip("Sabor que representa esta zona")]
    public Flavor flavor = Flavor.Vanilla;

    private IceCreamSource _source;

    void Awake()
    {
        _source = GetComponentInParent<IceCreamSource>();
        if (_source == null)
            Debug.LogError($"[IceCreamFlavor] '{gameObject.name}' no tiene IceCreamSource en su padre.");
    }

    void OnMouseDown()
    {
        // Marcar el click para que DragManager no lo procese de nuevo
        DragManager.Instance?.MarkClickHandled();
        _source?.RequestScoop(flavor);
    }
}