using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SISTEMA DE CLIENTES Y PEDIDOS — GDD secciones 4.6, 8.1, 8.2
/// Gestiona la cola de pedidos activos, el spawn progresivo de clientes
/// y la barra de paciencia de cada uno.
/// </summary>
public class OrderManager : MonoBehaviour
{
    [Header("Configuración MVP — GDD sección 7")]
    [Tooltip("Pedidos máximos simultáneos (empieza en 1, sube con dificultad)")]
    public int maxSimultaneousOrders = 4;

    [Tooltip("Paciencia inicial del cliente en segundos — GDD: 20-30s")]
    public float initialPatience = 30f;

    [Tooltip("Intervalo de llegada inicial en segundos — GDD: cada 20s")]
    public float initialSpawnInterval = 20f;

    [Header("Dificultad progresiva — GDD sección 8.2")]
    [Tooltip("Cada cuántos segundos de partida se aumenta la dificultad")]
    public float difficultyRampInterval = 30f;
    [Tooltip("Reducción de paciencia por rampa (segundos)")]
    public float patienceReduction = 3f;
    [Tooltip("Reducción del intervalo de spawn por rampa (segundos)")]
    public float spawnIntervalReduction = 2f;
    [Tooltip("Paciencia mínima posible (GDD: 12-15s al final)")]
    public float minPatience = 12f;
    [Tooltip("Intervalo de spawn mínimo (GDD: 8-10s al final)")]
    public float minSpawnInterval = 8f;

    [Header("Referencias")]
    public GameManager gameManager;
    public Transform[] orderSlotPositions; // Posiciones visuales de la cola de pedidos
    public GameObject orderCardPrefab;      // Prefab de tarjeta de pedido

    // ─── Estado interno ───────────────────────────────────────────
    private List<Order> _activeOrders = new List<Order>();
    private float _currentPatience;
    private float _currentSpawnInterval;
    private float _spawnTimer = 0f;
    private float _difficultyTimer = 0f;

    // ─────────────────────────────────────────────────────────────
    // INICIALIZACIÓN
    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        _currentPatience = initialPatience;
        _currentSpawnInterval = initialSpawnInterval;

        // Primera oleada: 1 cliente al inicio tras breve espera
        StartCoroutine(SpawnFirstCustomerAfterDelay(3f));
    }

    void Update()
    {
        if (!gameManager.IsGameRunning) return;

        // Actualizar paciencia de todos los pedidos activos
        UpdateOrderTimers();

        // Spawn de nuevos clientes
        _spawnTimer += Time.deltaTime;
        if (_spawnTimer >= _currentSpawnInterval && _activeOrders.Count < maxSimultaneousOrders)
        {
            _spawnTimer = 0f;
            SpawnOrder();
        }

        // Escalar dificultad con el tiempo
        _difficultyTimer += Time.deltaTime;
        if (_difficultyTimer >= difficultyRampInterval)
        {
            _difficultyTimer = 0f;
            IncreaseDifficulty();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // PEDIDOS
    // ─────────────────────────────────────────────────────────────

    private void SpawnOrder()
    {
        RecipeType recipe = GetRandomRecipe();
        Order newOrder = new Order
        {
            recipe       = recipe,
            timeLeft     = _currentPatience,
            maxTime      = _currentPatience,
            slotIndex    = GetFreeSlot(),
            isActive     = true
        };

        _activeOrders.Add(newOrder);
        RefreshOrderUI();

        Debug.Log($"[OrderManager] Nuevo pedido: {recipe} (paciencia: {_currentPatience}s)");
    }

    private void UpdateOrderTimers()
    {
        for (int i = _activeOrders.Count - 1; i >= 0; i--)
        {
            Order order = _activeOrders[i];
            order.timeLeft -= Time.deltaTime;

            // Actualizar visual de la barra de paciencia
            UpdateOrderCardTimer(order);

            // Cliente se va
            if (order.timeLeft <= 0f)
            {
                CustomerLeft(order);
                _activeOrders.RemoveAt(i);
            }
        }
    }

    private void CustomerLeft(Order order)
    {
        // GDD: "Si el temporizador llega a 0 → el cliente se va, penalizando al jugador"
        gameManager?.AddError();
        FeedbackManager.Instance?.ShowCustomerLeave(GetSlotPosition(order.slotIndex));
        AudioManager.Instance?.PlaySound(SoundType.CustomerLeave);
        RefreshOrderUI();

        Debug.Log($"[OrderManager] Cliente se fue sin su pedido: {order.recipe}");
    }

    /// <summary>
    /// Intenta cumplir un pedido de la cola.
    /// Devuelve true si se encontró coincidencia exacta.
    /// </summary>
    public bool TryFulfillOrder(RecipeType deliveredRecipe)
    {
        for (int i = 0; i < _activeOrders.Count; i++)
        {
            if (_activeOrders[i].recipe == deliveredRecipe)
            {
                Order fulfilled = _activeOrders[i];
                _activeOrders.RemoveAt(i);

                FeedbackManager.Instance?.ShowCustomerHappy(GetSlotPosition(fulfilled.slotIndex));
                AudioManager.Instance?.PlaySound(SoundType.CustomerHappy);
                RefreshOrderUI();

                Debug.Log($"[OrderManager] Pedido cumplido: {deliveredRecipe}");
                return true;
            }
        }

        return false; // Ningún pedido activo coincide
    }

    // ─────────────────────────────────────────────────────────────
    // DIFICULTAD PROGRESIVA
    // ─────────────────────────────────────────────────────────────

    private void IncreaseDifficulty()
    {
        _currentPatience      = Mathf.Max(minPatience, _currentPatience - patienceReduction);
        _currentSpawnInterval = Mathf.Max(minSpawnInterval, _currentSpawnInterval - spawnIntervalReduction);

        Debug.Log($"[OrderManager] Dificultad aumentada — Paciencia: {_currentPatience}s | Spawn: {_currentSpawnInterval}s");
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private RecipeType GetRandomRecipe()
    {
        // MVP: 4 recetas — GDD sección 7
        RecipeType[] recipes =
        {
            RecipeType.WaffleSimple,
            RecipeType.IceCreamAlone,
            RecipeType.WaffleWithIceCream,
            RecipeType.WaffleWithHoneyButter
        };
        return recipes[Random.Range(0, recipes.Length)];
    }

    private int GetFreeSlot()
    {
        bool[] used = new bool[maxSimultaneousOrders];
        foreach (var o in _activeOrders)
            if (o.slotIndex >= 0 && o.slotIndex < used.Length)
                used[o.slotIndex] = true;

        for (int i = 0; i < used.Length; i++)
            if (!used[i]) return i;

        return 0;
    }

    private Vector3 GetSlotPosition(int slotIndex)
    {
        if (orderSlotPositions != null && slotIndex < orderSlotPositions.Length)
            return orderSlotPositions[slotIndex].position;
        return transform.position;
    }

    private void RefreshOrderUI()
    {
        // Aquí se actualizaría la UI de las tarjetas de pedido
        // En el prototipo MVP, se usa Debug.Log + un sistema de UI básico
        Debug.Log($"[OrderManager] Pedidos activos: {_activeOrders.Count}");
    }

    private void UpdateOrderCardTimer(Order order)
    {
        // Comunicar el estado de la barra de paciencia al UI
        // Se implementa con referencia al OrderCard cuando existan los prefabs
    }

    private IEnumerator SpawnFirstCustomerAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SpawnOrder();
    }
}

// ─────────────────────────────────────────────────────────────────
// DATA CLASS
// ─────────────────────────────────────────────────────────────────

[System.Serializable]
public class Order
{
    public RecipeType recipe;
    public float timeLeft;
    public float maxTime;
    public int slotIndex;
    public bool isActive;
    public GameObject uiCard; // Referencia al GameObject de la tarjeta en UI
}
