using System;
using System.Collections.Generic;
using UnityEngine;

namespace Underworld.Inventory
{
    /// <summary>Gruppi di oggetti (per la UI e per i negozi).</summary>
    public enum ItemCategory { Misc, Consumable, Tool, Key, Clothing, Valuable }

    /// <summary>
    /// Un tipo di oggetto. Lo registra un modulo con ItemCatalog.Register, sia sul server sia sul client
    /// (il client lo usa solo per mostrare nome e descrizione). Prezzo e regole valgono solo sul server.
    /// </summary>
    public sealed class ItemDefinition
    {
        /// <summary>Id stabile: 2–48 caratteri a-z, 0-9, '.', '_', '-' (es. "food.burger").</summary>
        public string Id;
        /// <summary>Nome per il giocatore, in inglese.</summary>
        public string DisplayName;
        /// <summary>Descrizione breve per il giocatore, in inglese.</summary>
        public string Description;
        /// <summary>Prezzo in dollari interi (0 = non in vendita).</summary>
        public long Price;
        /// <summary>Quantità massima di questo oggetto nell'inventario (almeno 1).</summary>
        public int MaxStack = 1;
        public ItemCategory Category = ItemCategory.Misc;
        /// <summary>Id dei negozi che lo vendono (es. "downtown.shop.general"). Vuoto = nessuno.</summary>
        public string[] SoldAt = Array.Empty<string>();
        /// <summary>Il giocatore può usarlo (serve anche un IItemUseHandler sul server).</summary>
        public bool Usable;

        /// <summary>Null se la definizione va bene, altrimenti il motivo.</summary>
        public string Validate()
        {
            if (!ItemCatalog.IsValidId(Id)) return $"id non valido: '{Id}'";
            if (string.IsNullOrWhiteSpace(DisplayName)) return "nome mancante";
            if (Description == null) return "descrizione mancante";
            if (Price < 0) return "prezzo negativo";
            if (MaxStack < 1 || MaxStack > 100_000) return "MaxStack fuori da 1..100000";
            return null;
        }

        public bool IsSoldAt(string shopId)
        {
            if (shopId == null || SoldAt == null) return false;
            foreach (var s in SoldAt) if (s == shopId) return true;
            return false;
        }
    }

    /// <summary>
    /// Catalogo degli oggetti (contratto, sezione 14). Ogni modulo registra i suoi oggetti all'avvio, per esempio:
    /// <code>
    /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    /// static void RegisterItems() => ItemCatalog.Register(new ItemDefinition { Id = "food.burger", ... });
    /// </code>
    /// Un id già registrato = eccezione (niente doppioni silenziosi).
    /// </summary>
    public static class ItemCatalog
    {
        static readonly Dictionary<string, ItemDefinition> Items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        static readonly List<ItemDefinition> Ordered = new List<ItemDefinition>();

        public static IReadOnlyList<ItemDefinition> All => Ordered;

        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 2 || id.Length > 48) return false;
            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        public static void Register(ItemDefinition item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            string problem = item.Validate();
            if (problem != null) throw new ArgumentException($"Oggetto non valido ({item.Id}): {problem}");
            if (Items.ContainsKey(item.Id)) throw new InvalidOperationException($"Oggetto già registrato: {item.Id}");
            item.SoldAt ??= Array.Empty<string>();
            Items[item.Id] = item;
            Ordered.Add(item);
        }

        /// <summary>Registra solo se l'id non c'è ancora (utile per i test e per le registrazioni ripetute).</summary>
        public static bool TryRegister(ItemDefinition item)
        {
            if (item == null || Items.ContainsKey(item.Id ?? string.Empty)) return false;
            Register(item);
            return true;
        }

        public static bool Unregister(string id)
        {
            if (id == null || !Items.TryGetValue(id, out var item)) return false;
            Items.Remove(id);
            Ordered.Remove(item);
            return true;
        }

        public static bool TryGet(string id, out ItemDefinition item)
        {
            item = null;
            return id != null && Items.TryGetValue(id, out item);
        }

        public static ItemDefinition Get(string id) => TryGet(id, out var item) ? item : null;

        public static bool Contains(string id) => id != null && Items.ContainsKey(id);

        /// <summary>Oggetti venduti da un negozio.</summary>
        public static List<ItemDefinition> SoldBy(string shopId)
        {
            var list = new List<ItemDefinition>();
            foreach (var item in Ordered) if (item.IsSoldAt(shopId)) list.Add(item);
            return list;
        }

        /// <summary>Solo per i test EditMode.</summary>
        internal static void ClearForTests()
        {
            Items.Clear();
            Ordered.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ClearForTests();
    }

    public enum InventoryError
    {
        None,
        UnknownItem,
        InvalidCount,
        /// <summary>Supererebbe MaxStack dell'oggetto.</summary>
        StackFull,
        /// <summary>Troppi oggetti diversi (InventoryRules.MaxDistinctItems).</summary>
        NoSpace,
        NotEnough,
    }

    /// <summary>
    /// Regole dell'inventario su un dizionario id → quantità (quello di PlayerSave.inventory).
    /// Logica pura (test EditMode): nessuna rete, nessun salvataggio.
    /// </summary>
    public static class InventoryRules
    {
        /// <summary>Oggetti diversi al massimo in un inventario.</summary>
        public const int MaxDistinctItems = 40;

        public static int Count(IReadOnlyDictionary<string, int> inventory, string itemId) =>
            inventory != null && itemId != null && inventory.TryGetValue(itemId, out int n) ? n : 0;

        public static InventoryError CanAdd(IReadOnlyDictionary<string, int> inventory, ItemDefinition item, int count)
        {
            if (item == null) return InventoryError.UnknownItem;
            if (count <= 0) return InventoryError.InvalidCount;
            int have = Count(inventory, item.Id);
            if (have == 0 && inventory != null && inventory.Count >= MaxDistinctItems) return InventoryError.NoSpace;
            if ((long)have + count > item.MaxStack) return InventoryError.StackFull;
            return InventoryError.None;
        }

        public static InventoryError TryAdd(Dictionary<string, int> inventory, ItemDefinition item, int count)
        {
            var e = CanAdd(inventory, item, count);
            if (e != InventoryError.None) return e;
            inventory[item.Id] = Count(inventory, item.Id) + count;
            return InventoryError.None;
        }

        public static InventoryError TryRemove(Dictionary<string, int> inventory, string itemId, int count)
        {
            if (count <= 0) return InventoryError.InvalidCount;
            int have = Count(inventory, itemId);
            if (have < count) return InventoryError.NotEnough;
            if (have == count) inventory.Remove(itemId);
            else inventory[itemId] = have - count;
            return InventoryError.None;
        }
    }
}
