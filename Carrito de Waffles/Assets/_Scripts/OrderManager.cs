using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// ORDER MANAGER v2 — integra el sistema completo de clientes.
///
/// Cambios respecto a la versión anterior:
///   - Instancia prefabs de Customer en lugar de datos abstractos
///   - Los clientes llegan alternativamente de izquierda o derecha
///   - Solo 2 clientes simultáneos visibles (pedido de diseño)
///   - Al entregar pedido incorrecto → FlashError en TODOS los clientes activos
///   - Al entregar pedido correcto → ServeCorrect en el cliente que lo pidió
///   - Aplica el multiplicador de paciencia del ShopManager (decoraciones)
/// </summary>
public class OrderManager : MonoBehaviour
{
    [Header("══ Configuración MVP ══")]
    [Tooltip("Clientes simultáneos visibles — máx 2 según diseño")]
    public int maxSimultaneousCustomers = 2;
    [Tooltip("Paciencia base en segundos")]
    public float initialPatience = 30f;
    [Tooltip("Intervalo de llegada inicial")]
    public float initialSpawnInterval = 20f;

    [Header("══ Dificultad progresiva (GDD 8.2) ══")]
    public float difficultyRampInterval = 30f;
    public float patienceReduction      = 3f;
    public float spawnIntervalReduction = 2f;
    public float minPatience            = 12f;
    public float minSpawnInterval       = 8f;

    [Header("══ Posiciones de llegada ══")]
    [Tooltip("Transform del slot izquierdo (frente al mostrador)")]
    public Transform slotLeft;
    [Tooltip("Transform del slot derecho")]
    public Transform slotRight;

    [Header("══ Prefab de cliente ══")]
    public GameObject customerPrefab;

    [Header("══ Sprites de pedidos (globo del cliente) ══")]
    [Tooltip("Sprites en orden del enum RecipeType: WaffleSimple, IceCreamAlone, WaffleWithIceCream, WaffleWithHoneyButter")]
    public List<Sprite> recipeIcons;

    [Header("══ Referencias ══")]
    public GameManager gameManager;

    // ─── Estado interno ───────────────────────────────────────────
    private List<Customer> _activeCustomers  = new List<Customer>();
    private float _currentPatience;
    private float _currentSpawnInterval;
    private float _spawnTimer     = 0f;
    private float _difficultyTimer = 0f;
    private bool  _leftSlotFree   = true;
    private bool  _rightSlotFree  = true;

    // ─────────────────────────────────────────────────────────────
    // INICIALIZACIÓN
    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        _currentPatience      = initialPatience;
        _currentSpawnInterval = initialSpawnInterval;
        StartCoroutine(SpawnFirstCustomerAfterDelay(3f));
    }

    void Update()
    {
        if (gameManager == null || !gameManager.IsGameRunning) return;

        _spawnTimer      += Time.deltaTime;
        _difficultyTimer += Time.deltaTime;

        if (_spawnTimer >= _currentSpawnInterval && _activeCustomers.Count < maxSimultaneousCustomers)
        {
            _spawnTimer = 0f;
            TrySpawnCustomer();
        }

        if (_difficultyTimer >= difficultyRampInterval)
        {
            _difficultyTimer = 0f;
            IncreaseDifficulty();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // SPAWN
    // ─────────────────────────────────────────────────────────────

    private void TrySpawnCustomer()
    {
        if (customerPrefab == null) return;

        bool canLeft  = _leftSlotFree  && slotLeft  != null;
        bool canRight = _rightSlotFree && slotRight != null;
        if (!canLeft && !canRight) return;

        // Elegir lado disponible; si ambos están libres, aleatorio
        bool useLeft;
        if      (canLeft && canRight) useLeft = Random.value > 0.5f;
        else if (canLeft)             useLeft = true;
        else                          useLeft = false;

        Transform slot = useLeft ? slotLeft : slotRight;
        if (useLeft) _leftSlotFree  = false;
        else         _rightSlotFree = false;

        // Paciencia con multiplicador de decoraciones
        float patience = _currentPatience;
        if (ShopManager.Instance != null)
            patience *= ShopManager.Instance.GetPatienceMultiplier();

        // Instanciar
        GameObject go = Instantiate(customerPrefab, slot.position, Quaternion.identity);
        Customer customer = go.GetComponent<Customer>();

        if (customer != null)
        {
            RecipeType recipe = GetRandomRecipe();

            // Asignar sprite del pedido al globo
            Sprite icon = GetRecipeIcon(recipe);
            if (customer.orderIconRenderer != null && icon != null)
                customer.orderIconRenderer.sprite = icon;

            customer.Initialize(recipe, patience, useLeft, slot.position, this);
            _activeCustomers.Add(customer);

            Debug.Log($"[OrderManager] Cliente spawn: {recipe} desde {'{'}{(useLeft ? "izquierda" : "derecha")}{'}'} paciencia: {patience:F0}s");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // ENTREGA DE PEDIDOS — llamado desde DeliveryPlatform
    // ─────────────────────────────────────────────────────────────

    public bool TryFulfillOrder(RecipeType deliveredRecipe)
    {
        for (int i = 0; i < _activeCustomers.Count; i++)
        {
            Customer c = _activeCustomers[i];
            if (c == null || c.IsServed) continue;

            if (c.Order == deliveredRecipe)
            {
                // Pedido correcto → globo verde, cliente feliz
                c.ServeCorrect();
                _activeCustomers.RemoveAt(i);
                FreeSlot(c);
                Debug.Log($"[OrderManager] ✅ Pedido cumplido: {deliveredRecipe}");
                return true;
            }
        }

        // Ningún cliente tiene ese pedido → todos los globos en rojo
        foreach (var c in _activeCustomers)
            if (c != null && !c.IsServed) c.FlashError();

        Debug.Log($"[OrderManager] ❌ Pedido incorrecto: {deliveredRecipe}");
        return false;
    }

    /// <summary>
    /// Llamado por Customer.cs cuando un cliente se va (tiempo agotado).
    /// </summary>
    public void OnCustomerLeft(Customer customer, bool served)
    {
        _activeCustomers.Remove(customer);
        FreeSlot(customer);

        if (!served)
        {
            gameManager?.AddError();
            FeedbackManager.Instance?.ShowCustomerLeave(customer.transform.position);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // DIFICULTAD PROGRESIVA
    // ─────────────────────────────────────────────────────────────

    private void IncreaseDifficulty()
    {
        _currentPatience      = Mathf.Max(minPatience,      _currentPatience      - patienceReduction);
        _currentSpawnInterval = Mathf.Max(minSpawnInterval, _currentSpawnInterval - spawnIntervalReduction);
        Debug.Log($"[OrderManager] Dificultad ↑ — Paciencia: {_currentPatience}s | Spawn: {_currentSpawnInterval}s");
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private void FreeSlot(Customer customer)
    {
        if (slotLeft  != null && Vector3.Distance(customer.targetPosition, slotLeft.position)  < 0.5f) _leftSlotFree  = true;
        if (slotRight != null && Vector3.Distance(customer.targetPosition, slotRight.position) < 0.5f) _rightSlotFree = true;
    }

    private RecipeType GetRandomRecipe()
    {
        RecipeType[] all =
        {
            RecipeType.WaffleSimple,
            RecipeType.IceCreamAlone,
            RecipeType.WaffleWithIceCream,
            RecipeType.WaffleWithHoneyButter
        };
        return all[Random.Range(0, all.Length)];
    }

    private Sprite GetRecipeIcon(RecipeType recipe)
    {
        int index = (int)recipe;
        return (recipeIcons != null && index < recipeIcons.Count) ? recipeIcons[index] : null;
    }

    private IEnumerator SpawnFirstCustomerAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        TrySpawnCustomer();
    }
}
