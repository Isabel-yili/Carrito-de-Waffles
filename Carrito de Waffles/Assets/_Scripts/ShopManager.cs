using UnityEngine;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════
// TIPOS DE MEJORAS disponibles en la tienda
// ═══════════════════════════════════════════════════════════════════
public enum UpgradeType
{
    // GDD sección 8.3 — mejoras existentes
    ExtraOven,          // Desbloquea wafflera adicional ($150 c/u, máx 3)
    ExtraPlateSlot,     // Ranura extra de pedido ($100 c/u, máx 4)
    FasterCooking,      // Reduce tiempo de cocción 6s→5s→4s ($200 c/u, máx 2)
    PatienceBoost,      // +20% paciencia base de clientes ($300, máx 1) — futuro GDD

    // DECORACIONES — nuevas, afectan la paciencia si hay 3 o más compradas
    Decoration_Flowers,     // Flores en el mostrador
    Decoration_Lights,      // Luces extra en el carrito
    Decoration_Sign,        // Letrero decorativo
    Decoration_Umbrella,    // Sombrilla de colores
    Decoration_Chalkboard,  // Pizarrón con el menú del día
    Decoration_Planters,    // Macetas con plantas
}

/// <summary>
/// TIENDA DE MEJORAS — GDD sección 6.4 y 8.3
///
/// Nueva regla de decoraciones (pedido en diseño):
///   Si el jugador tiene 3 o más decoraciones compradas,
///   todos los clientes llegan con +25% de paciencia base.
///   Esto se aplica automáticamente al instanciar cada cliente.
///
/// JERARQUÍA DE ESCENA:
///   [Managers]
///   └── ShopManager   (este script)
///
///   [UI_CANVAS]
///   └── ShopPanel     (se abre desde el Menú Principal o entre partidas)
///       ├── UpgradeList   (lista de tarjetas de mejora)
///       └── CloseButton
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    // ─── Definición de mejoras ────────────────────────────────────
    [System.Serializable]
    public class UpgradeData
    {
        public UpgradeType type;
        public string      displayName;
        [TextArea(2, 3)]
        public string      description;
        public int         baseCost;        // Costo del nivel 1
        public int         maxLevel;        // Niveles máximos (1 = solo se compra una vez)
        public bool        isDecoration;    // Si es decoración, cuenta para el bonus
        public GameObject  worldObject;     // Objeto en escena que se activa al comprar
    }

    [Header("══ Catálogo de mejoras ══")]
    public List<UpgradeData> upgrades;

    [Header("══ Bonus de decoraciones ══")]
    [Tooltip("Cuántas decoraciones se necesitan para activar el bonus de paciencia")]
    public int decorationsForBonus = 3;
    [Tooltip("Multiplicador de paciencia cuando el bonus está activo")]
    public float decorationPatienceBonus = 1.25f;

    [Header("══ Referencias de hornos ══")]
    [Tooltip("Los GameObjects de Oven_02 y Oven_03 en escena")]
    public List<Oven> extraOvens;

    [Header("══ UI de la tienda ══")]
    public GameObject shopPanel;

    // ─── Estado persistente ───────────────────────────────────────
    // Diccionario: UpgradeType → nivel actual comprado
    private Dictionary<UpgradeType, int> _purchasedLevels = new Dictionary<UpgradeType, int>();

    // Cache de decoraciones compradas
    private int _decorationsBought = 0;
    public  int DecorationsBought => _decorationsBought;
    public  bool DecorationBonusActive => _decorationsBought >= decorationsForBonus;

    // ─────────────────────────────────────────────────────────────
    // INICIALIZACIÓN
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Inicializar todos los niveles a 0
        foreach (var u in upgrades)
            _purchasedLevels[u.type] = 0;
    }

    void Start()
    {
        if (shopPanel != null) shopPanel.SetActive(false);
        // Asegurarse de que los hornos extra empiezan bloqueados
        foreach (var oven in extraOvens)
            if (oven != null) oven.gameObject.SetActive(false); // El LockedOverlay maneja el visual
    }

    // ─────────────────────────────────────────────────────────────
    // API PÚBLICA
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Intenta comprar una mejora. Devuelve true si la compra fue exitosa.
    /// </summary>
    public bool TryPurchase(UpgradeType type)
    {
        UpgradeData data = GetUpgradeData(type);
        if (data == null) return false;

        int currentLevel = GetLevel(type);
        if (currentLevel >= data.maxLevel)
        {
            Debug.Log($"[Shop] {type} ya está al máximo (nivel {currentLevel})");
            return false;
        }

        int cost = GetCost(type);
        if (GameManager.Instance == null || GameManager.Instance.CurrentMoney < cost)
        {
            Debug.Log($"[Shop] Dinero insuficiente para {type} (cuesta ${cost})");
            return false;
        }

        // Descontar dinero y subir nivel
        GameManager.Instance.DeductMoney(cost);
        _purchasedLevels[type] = currentLevel + 1;

        // Aplicar efecto de la mejora
        ApplyUpgrade(type, _purchasedLevels[type]);

        // Contar decoraciones
        if (data.isDecoration)
        {
            _decorationsBought++;
            if (DecorationBonusActive)
                Debug.Log($"[Shop] ¡Bonus de decoraciones activado! ({_decorationsBought} decoraciones)");
        }

        AudioManager.Instance?.PlaySound(SoundType.DeliverySuccess);
        Debug.Log($"[Shop] Comprado: {type} nivel {_purchasedLevels[type]} por ${cost}");
        return true;
    }

    public int  GetLevel(UpgradeType type) => _purchasedLevels.TryGetValue(type, out int v) ? v : 0;
    public bool IsMaxLevel(UpgradeType type)
    {
        var data = GetUpgradeData(type);
        return data != null && GetLevel(type) >= data.maxLevel;
    }

    public int GetCost(UpgradeType type)
    {
        var data = GetUpgradeData(type);
        if (data == null) return 0;
        // El costo aumenta con el nivel (nivel 2 cuesta el doble, etc.)
        return data.baseCost * (GetLevel(type) + 1);
    }

    /// <summary>
    /// Retorna el multiplicador de paciencia a aplicar al instanciar un cliente.
    /// Considera el bonus de decoraciones y la mejora de PatienceBoost.
    /// </summary>
    public float GetPatienceMultiplier()
    {
        float mult = 1f;

        // Bonus de decoraciones
        if (DecorationBonusActive)
            mult *= decorationPatienceBonus;

        // Mejora directa de paciencia (futuro)
        int patienceLevel = GetLevel(UpgradeType.PatienceBoost);
        if (patienceLevel > 0)
            mult *= (1f + 0.2f * patienceLevel);

        return mult;
    }

    /// <summary>
    /// Retorna el tiempo de cocción actual según mejoras compradas.
    /// Base: 6s → nivel 1: 5s → nivel 2: 4s
    /// </summary>
    public float GetCookingTime(float baseCookingTime = 6f)
    {
        int level = GetLevel(UpgradeType.FasterCooking);
        return baseCookingTime - level;
    }

    // ─────────────────────────────────────────────────────────────
    // APLICAR EFECTOS
    // ─────────────────────────────────────────────────────────────

    private void ApplyUpgrade(UpgradeType type, int newLevel)
    {
        switch (type)
        {
            case UpgradeType.ExtraOven:
                // Desbloquear el siguiente horno en la lista
                int ovenIndex = newLevel - 1;
                if (ovenIndex < extraOvens.Count && extraOvens[ovenIndex] != null)
                {
                    extraOvens[ovenIndex].Unlock();
                    Debug.Log($"[Shop] Horno {ovenIndex + 2} desbloqueado");
                }
                break;

            case UpgradeType.FasterCooking:
                // Los hornos leen GetCookingTime() dinámicamente — no hace falta más
                Debug.Log($"[Shop] Tiempo de cocción reducido a {GetCookingTime()}s");
                break;

            default:
                // Decoraciones y otras mejoras: activar GameObject en escena
                var data = GetUpgradeData(type);
                if (data?.worldObject != null)
                    data.worldObject.SetActive(true);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // UI DE LA TIENDA
    // ─────────────────────────────────────────────────────────────

    public void OpenShop()
    {
        if (shopPanel != null) shopPanel.SetActive(true);
        Time.timeScale = 0f; // Pausar mientras la tienda está abierta
    }

    public void CloseShop()
    {
        if (shopPanel != null) shopPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private UpgradeData GetUpgradeData(UpgradeType type)
    {
        return upgrades?.Find(u => u.type == type);
    }
}
