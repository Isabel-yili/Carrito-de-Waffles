using UnityEngine;

/// <summary>
/// WAFFLE CHILD CLICK RELAY — compatibilidad con prefabs existentes.
///
/// Con la arquitectura v4 de Oven.cs, los hijos del WaffleDisplay
/// ya NO tienen Collider2D activos, por lo que OnMouseDown nunca se dispara
/// en ellos. Este script se conserva para no romper prefabs existentes,
/// pero su lógica activa es mínima.
///
/// Si el jugador hace click sobre el horno (Oven.OnMouseDown) o sobre el
/// WaffleDisplay raíz (Oven.OnMouseDown via Collider2D del raíz),
/// RequestExtract() se llama desde ahí.
///
/// SETUP: mantener en los hijos del WaffleDisplay si ya existe,
/// pero asegurarse de que esos hijos NO tienen Collider2D activos.
/// Oven.Awake() desactiva automáticamente los colliders de hijos.
/// </summary>
public class WaffleChildClickRelay : MonoBehaviour
{
    private Oven _oven;

    void Awake()
    {
        _oven = GetComponentInParent<Oven>();
    }

    void OnMouseDown()
    {
        // Si el WaffleDisplay ya fue desparentizado (carry activo),
        // el DraggableItem en el raíz maneja el input. No hacer nada.
        DraggableItem draggableRoot = GetComponentInParent<DraggableItem>();
        if (draggableRoot != null && draggableRoot.IsBeingCarried) return;

        // Buscar Oven dinámicamente por si el caché falló
        Oven oven = _oven != null ? _oven : GetComponentInParent<Oven>();
        if (oven == null) return;

        Debug.Log($"[WaffleChildClickRelay] Click en {gameObject.name} → RequestExtract");
        oven.RequestExtract();
    }
}