using UnityEngine;

/// <summary>
/// DIAGNÓSTICO TEMPORAL — adjuntar al mismo GameObject que Oven.
/// Borra este script una vez que el waffle funcione.
///
/// Qué hace: intercepta todos los clicks en la escena y reporta
/// exactamente qué objeto recibe el raycast, qué estado tiene el Oven,
/// y si DragManager existe.
/// </summary>
public class OvenDiagnostic : MonoBehaviour
{
    private Oven _oven;

    void Awake()
    {
        _oven = GetComponent<Oven>();
    }

    void Start()
    {
        Debug.Log($"[DIAG] Oven encontrado: {_oven != null}");
        Debug.Log($"[DIAG] DragManager encontrado: {DragManager.Instance != null}");

        if (_oven != null)
        {
            // Usa reflexión para leer el campo privado waffleDisplay
            var field = typeof(Oven).GetField("waffleDisplay",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                object val = field.GetValue(_oven);
                Debug.Log($"[DIAG] waffleDisplay asignado: {val != null} | tipo: {(val != null ? val.GetType().Name : "NULL")}");
            }
        }

        // Reportar colliders en el Oven y sus hijos
        Collider2D[] cols = GetComponentsInChildren<Collider2D>(true);
        Debug.Log($"[DIAG] Collider2D encontrados en Oven (incluye hijos): {cols.Length}");
        foreach (var c in cols)
            Debug.Log($"[DIAG]   Collider en: '{c.gameObject.name}' | isTrigger: {c.isTrigger} | enabled: {c.enabled} | gameObject active: {c.gameObject.activeSelf}");
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        // Raycast 2D en la posición del mouse
        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Collider2D[] hits = new Collider2D[16];
        ContactFilter2D filter = new ContactFilter2D().NoFilter();
        int count = Physics2D.OverlapPoint(mouseWorld, filter, hits);

        Debug.Log($"[DIAG] Click en {mouseWorld} | Objetos tocados: {count}");
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            Debug.Log($"[DIAG]   [{i}] '{hits[i].gameObject.name}' " +
                      $"(padre: '{hits[i].transform.parent?.name ?? "ninguno"}') " +
                      $"| isTrigger: {hits[i].isTrigger}");
        }

        if (_oven != null)
            Debug.Log($"[DIAG] Estado del Oven al click: {_oven.State}");
    }
}