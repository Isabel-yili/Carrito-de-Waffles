using UnityEngine;

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