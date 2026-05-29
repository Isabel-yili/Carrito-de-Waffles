using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// ORDER MANAGER v3 — soporte para todas las combinaciones de recetas.
///
/// Cambios respecto a v2:
///   - recipeIcons (List<Sprite> indexada por enum int) reemplazada por
///     List<RecipeIconEntry> (pares RecipeType → Sprite), lo que permite
///     asignar sprites a cualquiera de las 31+ recetas disponibles sin
///     depender de la posición numérica del enum.
///   - GetRandomRecipe() ahora sortea entre TODAS las recetas configuradas
///     en recipeIcons (solo las que tienen sprite asignado), así el diseñador
///     controla qué pedidos aparecen en juego simplemente rellenando o
///     dejando vacíos los slots del Inspector.
///   - GetRecipeIcon() usa búsqueda por RecipeType en lugar de índice.
///   - El resto del comportamiento (dificultad progresiva, slots, clientes)
///     permanece igual que en v2.
/// </summary>
public class OrderManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // ENTRADA DE SPRITE POR RECETA
    // ─────────────────────────────────────────────────────────────

    [System.Serializable]
    public class RecipeIconEntry
    {
        [Tooltip("Tipo de receta. Coincide con PlateRecipe.ToSpriteKey() y con el enum RecipeType.")]
        public RecipeType recipeType;

        [Tooltip("Sprite que se muestra en el globo de pedido del cliente.")]
        public Sprite icon;
    }

    // ─────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────

    [Header("══ Configuración MVP ══")]
    [Tooltip("Clientes simultáneos visibles — máx 2 según diseño")]
    public int maxSimultaneousCustomers = 2;
    [Tooltip("Paciencia base en segundos")]
    public float initialPatience = 30f;
    [Tooltip("Intervalo de llegada inicial")]
    public float initialSpawnInterval = 20f;

    [Header("══ Dificultad progresiva (GDD 8.2) ══")]
    public float difficultyRampInterval = 30f;
    public float patienceReduction = 3f;
    public float spawnIntervalReduction = 2f;
    public float minPatience = 12f;
    public float minSpawnInterval = 8f;

    [Header("══ Posiciones de llegada ══")]
    [Tooltip("Transform del slot izquierdo (frente al mostrador)")]
    public Transform slotLeft;
    [Tooltip("Transform del slot derecho")]
    public Transform slotRight;

    [Header("══ Spawn Delay ══")]
    [Tooltip("Tiempo mínimo antes de que llegue otro cliente.")]
    public float minCustomerSpawnDelay = 1f;

    [Tooltip("Tiempo máximo antes de que llegue otro cliente.")]
    public float maxCustomerSpawnDelay = 2f;

    [Header("══ Prefabs de clientes ══")]
    [Tooltip("Lista de prefabs de clientes. Se elige uno al azar en cada spawn.\nAgrega aquí todos los diseños de cliente que tengas.")]
    public List<GameObject> customerPrefabs = new List<GameObject>();

    [Header("══ Sprites de pedidos (globo del cliente) ══")]
    [Tooltip(
        "Asigna un sprite por cada receta que quieras que pidan los clientes.\n\n" +
        "IMPORTANTE: solo las entradas con sprite asignado entrarán al sorteo de\n" +
        "pedidos aleatorios. Las entradas con sprite vacío se ignoran.\n\n" +
        "Orden recomendado en Inspector (31 recetas):\n" +
        "── Waffle solo ──────────────────────────────\n" +
        "  Perfect\n" +
        "── Waffle + 1 topping ───────────────────────\n" +
        "  Perfect_Vanilla\n" +
        "  Perfect_Strawberry\n" +
        "  Perfect_Chocolate\n" +
        "  Perfect_Honey\n" +
        "── Waffle + 2 toppings ──────────────────────\n" +
        "  Perfect_VanillaStrawberry\n" +
        "  Perfect_VanillaChocolate\n" +
        "  Perfect_VanillaHoney\n" +
        "  Perfect_StrawberryChocolate\n" +
        "  Perfect_StrawberryHoney\n" +
        "  Perfect_ChocolateHoney\n" +
        "── Waffle + 3 toppings ──────────────────────\n" +
        "  Perfect_VanillaStrawberryChocolate\n" +
        "  Perfect_VanillaStrawberryHoney\n" +
        "  Perfect_VanillaChocolateHoney\n" +
        "  Perfect_StrawberryChocolateHoney\n" +
        "── Waffle + 4 toppings ──────────────────────\n" +
        "  Perfect_VanillaStrawberryChocolateHoney\n" +
        "── Helado solo ──────────────────────────────\n" +
        "  IceCream_Vanilla\n" +
        "  IceCream_Strawberry\n" +
        "  IceCream_Chocolate\n" +
        "  IceCream_VanillaStrawberry\n" +
        "  IceCream_VanillaChocolate\n" +
        "  IceCream_VanillaHoney\n" +
        "  IceCream_StrawberryChocolate\n" +
        "  IceCream_StrawberryHoney\n" +
        "  IceCream_ChocolateHoney\n" +
        "  IceCream_VanillaStrawberryChocolate\n" +
        "  IceCream_VanillaStrawberryHoney\n" +
        "  IceCream_VanillaChocolateHoney\n" +
        "  IceCream_StrawberryChocolateHoney\n" +
        "  IceCream_VanillaStrawberryChocolateHoney\n" +
        "── Miel sola ────────────────────────────────\n" +
        "  Honey")]
    public List<RecipeIconEntry> recipeIcons = new List<RecipeIconEntry>();

    [Header("══ Referencias ══")]
    public GameManager gameManager;

    // ─── Estado interno ───────────────────────────────────────────
    private List<Customer> _activeCustomers = new List<Customer>();
    private float _currentPatience;
    private float _currentSpawnInterval;
    private float _spawnTimer = 0f;
    private float _difficultyTimer = 0f;
    private bool _leftSlotFree = true;
    private bool _rightSlotFree = true;
    private bool _spawnQueued = false;

    // Cache de recetas disponibles (las que tienen sprite asignado)
    private List<RecipeType> _availableRecipes = new List<RecipeType>();

    // Referencia directa al slot que ocupa cada cliente (evita comparar posiciones flotantes)
    private Dictionary<Customer, Transform> _customerSlot = new Dictionary<Customer, Transform>();

    // ─────────────────────────────────────────────────────────────
    // INICIALIZACIÓN
    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        _currentPatience = initialPatience;
        _currentSpawnInterval = initialSpawnInterval;

        BuildAvailableRecipesCache();
        LogSlotDiagnostics();

        StartCoroutine(SpawnFirstCustomerAfterDelay(3f));
    }

    /// <summary>
    /// Imprime en Console el estado de los slots para diagnosticar problemas de posición.
    /// Se llama automáticamente en Start(). También puedes llamarla desde el Inspector
    /// con el botón de contexto si agregas [ContextMenu].
    /// </summary>
    [ContextMenu("Diagnosticar Slots")]
    public void LogSlotDiagnostics()
    {
        Debug.Log("[OrderManager] ══ DIAGNÓSTICO DE SLOTS ══");
        Debug.Log($"[OrderManager]   slotLeft  asignado: {slotLeft  != null} | " +
                  $"posición: {(slotLeft  != null ? slotLeft.position.ToString()  : "N/A")}");
        Debug.Log($"[OrderManager]   slotRight asignado: {slotRight != null} | " +
                  $"posición: {(slotRight != null ? slotRight.position.ToString() : "N/A")}");
        Debug.Log($"[OrderManager]   _leftSlotFree={_leftSlotFree} | _rightSlotFree={_rightSlotFree}");
        Debug.Log($"[OrderManager]   Prefabs configurados: {customerPrefabs.Count}");
        for (int i = 0; i < customerPrefabs.Count; i++)
            Debug.Log($"[OrderManager]     [{i}] {(customerPrefabs[i] != null ? customerPrefabs[i].name : "NULL")}");
        Debug.Log("[OrderManager] ══════════════════════════════");
    }

    /// <summary>
    /// Construye la lista de recetas que entran al sorteo: solo las que
    /// tienen un sprite asignado en el Inspector.
    /// </summary>
    private void BuildAvailableRecipesCache()
    {
        _availableRecipes.Clear();

        if (recipeIcons == null || recipeIcons.Count == 0)
        {
            // Fallback de seguridad: usar las 4 recetas básicas sin sprite
            Debug.LogWarning("[OrderManager] recipeIcons vacío — usando recetas de fallback sin icono.");
            _availableRecipes.Add(RecipeType.Perfect);
            _availableRecipes.Add(RecipeType.IceCream_Vanilla);
            _availableRecipes.Add(RecipeType.Perfect_Vanilla);
            _availableRecipes.Add(RecipeType.Perfect_Honey);
            return;
        }

        foreach (var entry in recipeIcons)
        {
            if (entry.icon != null)
                _availableRecipes.Add(entry.recipeType);
        }

        if (_availableRecipes.Count == 0)
        {
            Debug.LogWarning("[OrderManager] Todas las entradas de recipeIcons tienen sprite nulo — " +
                             "asigna al menos un sprite en el Inspector para que los clientes pidan algo.");
        }
        else
        {
            Debug.Log($"[OrderManager] {_availableRecipes.Count} receta(s) disponibles para pedidos.");
        }
    }

    void Update()
    {
        if (gameManager == null || !gameManager.IsGameRunning) return;

        _spawnTimer += Time.deltaTime;
        _difficultyTimer += Time.deltaTime;

        if (_spawnTimer >= _currentSpawnInterval &&
            _activeCustomers.Count < maxSimultaneousCustomers)
        {
            _spawnTimer = 0f;

            StartCoroutine(SpawnCustomerWithDelay());
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
        // ── Validar que haya al menos un prefab configurado ──
        if (customerPrefabs == null || customerPrefabs.Count == 0)
        {
            Debug.LogError("[OrderManager] No hay prefabs de clientes configurados en la lista 'customerPrefabs'.");
            return;
        }
        if (_availableRecipes.Count == 0) return;

        bool canLeft  = _leftSlotFree  && slotLeft  != null;
        bool canRight = _rightSlotFree && slotRight != null;

        // ── LOG de diagnóstico: estado de slots antes de decidir ──
        Debug.Log($"[OrderManager] TrySpawn → canLeft={canLeft} (free={_leftSlotFree}, assigned={slotLeft != null}) | " +
                  $"canRight={canRight} (free={_rightSlotFree}, assigned={slotRight != null})");

        if (!canLeft && !canRight)
        {
            Debug.Log("[OrderManager] Ambos slots ocupados — spawn cancelado.");
            return;
        }

        // Elegir lado disponible; si ambos están libres, 50/50 con log
        bool useLeft;
        if (canLeft && canRight)
        {
            float roll = Random.value;
            useLeft = (roll > 0.5f);
            Debug.Log($"[OrderManager] Ambos libres — roll={roll:F3} → {(useLeft ? "IZQUIERDO" : "DERECHO")}");
        }
        else if (canLeft)
        {
            useLeft = true;
            Debug.Log("[OrderManager] Solo slot IZQUIERDO libre.");
        }
        else
        {
            useLeft = false;
            Debug.Log("[OrderManager] Solo slot DERECHO libre.");
        }

        Transform slot = useLeft ? slotLeft : slotRight;

        // Marcar el slot como ocupado ANTES de instanciar
        if (useLeft) _leftSlotFree  = false;
        else         _rightSlotFree = false;

        // Paciencia con multiplicador de decoraciones
        float patience = _currentPatience;
        if (ShopManager.Instance != null)
            patience *= ShopManager.Instance.GetPatienceMultiplier();

        // ── Seleccionar prefab aleatorio de la lista ──
        GameObject chosenPrefab = GetRandomCustomerPrefab();
        if (chosenPrefab == null)
        {
            if (useLeft) _leftSlotFree  = true;
            else         _rightSlotFree = true;
            Debug.LogError("[OrderManager] No se pudo obtener un prefab válido de la lista.");
            return;
        }

        // Instanciar en la posición del slot
        Debug.Log($"[OrderManager] Instanciando '{chosenPrefab.name}' en posición {slot.position}");
        GameObject go = Instantiate(chosenPrefab, slot.position, Quaternion.identity);
        Customer customer = go.GetComponent<Customer>();

        if (customer != null)
        {
            RecipeType recipe = GetRandomRecipe();

            // Asignar sprite del pedido al globo
            Sprite icon = GetRecipeIcon(recipe);
            if (customer.orderIconRenderer != null && icon != null)
                customer.orderIconRenderer.sprite = icon;

            customer.Initialize(recipe, patience, slot.position, this, useLeft);
            _activeCustomers.Add(customer);

            // Guardar referencia directa al slot ocupado
            _customerSlot[customer] = slot;

            Debug.Log($"[OrderManager] ✅ Cliente '{chosenPrefab.name}' spawneado: {recipe} | " +
                      $"slot={( useLeft ? "IZQUIERDO" : "DERECHO")} | pos={slot.position} | paciencia={patience:F0}s");
        }
        else
        {
            // Liberar el slot si el prefab no tenía componente Customer
            if (useLeft) _leftSlotFree  = true;
            else         _rightSlotFree = true;
            Debug.LogError($"[OrderManager] El prefab '{chosenPrefab.name}' no tiene el componente Customer.");
        }
    }

    /// <summary>
    /// Selecciona un prefab al azar de customerPrefabs, ignorando entradas nulas.
    /// </summary>
    private GameObject GetRandomCustomerPrefab()
    {
        // Filtrar nulos para evitar errores si alguna entrada quedó vacía
        List<GameObject> valid = new List<GameObject>();
        foreach (var p in customerPrefabs)
            if (p != null) valid.Add(p);

        if (valid.Count == 0) return null;
        return valid[Random.Range(0, valid.Count)];
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
        // + penalización de paciencia (10% de la paciencia máxima actual)
        foreach (var c in _activeCustomers)
        {
            if (c == null || c.IsServed) continue;
            c.FlashError();
            c.PenalizePatience(_currentPatience * 0.10f);
        }

        Debug.Log($"[OrderManager] ❌ Pedido incorrecto: {deliveredRecipe} — " +
                  $"paciencia penalizada en todos los clientes.");
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
        _currentPatience = Mathf.Max(minPatience, _currentPatience - patienceReduction);
        _currentSpawnInterval = Mathf.Max(minSpawnInterval, _currentSpawnInterval - spawnIntervalReduction);
        Debug.Log($"[OrderManager] Dificultad ↑ — Paciencia: {_currentPatience}s | Spawn: {_currentSpawnInterval}s");
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private void FreeSlot(Customer customer)
    {
        // ── BUG FIX 4: usar referencia directa al slot en lugar de comparar posiciones flotantes ──
        if (_customerSlot.TryGetValue(customer, out Transform occupiedSlot))
        {
            if (occupiedSlot == slotLeft)       _leftSlotFree  = true;
            else if (occupiedSlot == slotRight) _rightSlotFree = true;
            _customerSlot.Remove(customer);
        }
        else
        {
            // Fallback por compatibilidad (tolerancia generosa de 1 unidad)
            if (slotLeft  != null && Vector3.Distance(customer.targetPosition, slotLeft.position)  < 1f)
                _leftSlotFree  = true;
            if (slotRight != null && Vector3.Distance(customer.targetPosition, slotRight.position) < 1f)
                _rightSlotFree = true;
        }
    }

    /// <summary>
    /// Devuelve una receta aleatoria entre las que tienen sprite configurado.
    /// El diseñador controla el pool simplemente asignando o dejando vacíos
    /// los sprites en el Inspector.
    /// </summary>
    private RecipeType GetRandomRecipe()
    {
        if (_availableRecipes.Count == 0)
            return RecipeType.Perfect; // fallback absoluto

        return _availableRecipes[Random.Range(0, _availableRecipes.Count)];
    }

    /// <summary>
    /// Busca el sprite correspondiente al RecipeType dado.
    /// Usa búsqueda lineal (lista pequeña, no necesita diccionario).
    /// </summary>
    private Sprite GetRecipeIcon(RecipeType recipe)
    {
        if (recipeIcons == null) return null;

        foreach (var entry in recipeIcons)
        {
            if (entry.recipeType == recipe)
                return entry.icon;
        }

        return null;
    }

    private IEnumerator SpawnFirstCustomerAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        TrySpawnCustomer();
    }

    private IEnumerator SpawnCustomerWithDelay()
    {
        if (_spawnQueued)
            yield break;

        _spawnQueued = true;

        float delay = Random.Range(minCustomerSpawnDelay, maxCustomerSpawnDelay);

        Debug.Log($"[OrderManager] Esperando {delay:F2}s antes del próximo cliente.");

        yield return new WaitForSeconds(delay);

        TrySpawnCustomer();

        _spawnQueued = false;
    }
}