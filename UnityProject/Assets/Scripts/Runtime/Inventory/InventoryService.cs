using System;
using System.Collections.Generic;
using Underworld.Server;
using UnityEngine;

namespace Underworld.Inventory
{
    /// <summary>
    /// Gestore dell'uso di un oggetto (punto di estensione). Lo registra il modulo che possiede l'oggetto:
    /// <c>ItemUseHandlers.Register("food.burger", new BurgerHandler());</c> (solo sul server).
    /// TryUse fa l'effetto e restituisce vero se è riuscito: solo allora il server toglie l'oggetto.
    /// message: testo breve in inglese per il giocatore (può essere null).
    /// </summary>
    public interface IItemUseHandler
    {
        bool TryUse(PlayerSession session, ItemDefinition item, out string message);
    }

    /// <summary>Registro dei gestori d'uso: per id dell'oggetto, poi per categoria. Solo server.</summary>
    public static class ItemUseHandlers
    {
        static readonly Dictionary<string, IItemUseHandler> ById = new Dictionary<string, IItemUseHandler>(StringComparer.Ordinal);
        static readonly Dictionary<ItemCategory, IItemUseHandler> ByCategory = new Dictionary<ItemCategory, IItemUseHandler>();

        public static void Register(string itemId, IItemUseHandler handler)
        {
            if (!ItemCatalog.IsValidId(itemId)) throw new ArgumentException($"Id non valido: '{itemId}'", nameof(itemId));
            ById[itemId] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public static void RegisterForCategory(ItemCategory category, IItemUseHandler handler) =>
            ByCategory[category] = handler ?? throw new ArgumentNullException(nameof(handler));

        public static bool Unregister(string itemId) => itemId != null && ById.Remove(itemId);

        public static bool TryGet(ItemDefinition item, out IItemUseHandler handler)
        {
            handler = null;
            if (item == null) return false;
            return ById.TryGetValue(item.Id, out handler) || ByCategory.TryGetValue(item.Category, out handler);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ById.Clear();
            ByCategory.Clear();
        }
    }

    public enum UseResult { Used, UnknownItem, NotUsable, NotOwned, NoHandler, Failed }

    /// <summary>
    /// Inventario dei giocatori sul server (PlayerSave.inventory). Solo server.
    /// Ogni cambio: sessione da salvare (2 s) e copia al proprietario (PlayerInventory).
    /// reason: testo breve per il log, es. "shop.buy", "mission.reward".
    /// </summary>
    public sealed class InventoryService
    {
        /// <summary>Inventario cambiato: sessione, id, variazione, motivo.</summary>
        public event Action<PlayerSession, string, int, string> ItemsChanged;

        public int Count(PlayerSession session, string itemId) =>
            session?.Save == null ? 0 : InventoryRules.Count(session.Save.Inventory, itemId);

        public IReadOnlyDictionary<string, int> Items(PlayerSession session) => session?.Save?.Inventory;

        public InventoryError CanAdd(PlayerSession session, string itemId, int count)
        {
            if (session?.Save == null || session.Closed) return InventoryError.NoSpace;
            if (!ItemCatalog.TryGet(itemId, out var item)) return InventoryError.UnknownItem;
            return InventoryRules.CanAdd(session.Save.Inventory, item, count);
        }

        public bool TryAdd(PlayerSession session, string itemId, int count, string reason) =>
            TryAdd(session, itemId, count, reason, out _);

        public bool TryAdd(PlayerSession session, string itemId, int count, string reason, out InventoryError error)
        {
            error = CanAdd(session, itemId, count);
            if (error != InventoryError.None) return false;
            error = InventoryRules.TryAdd(session.Save.Inventory, ItemCatalog.Get(itemId), count);
            if (error != InventoryError.None) return false;
            Changed(session, itemId, count, reason);
            return true;
        }

        public bool TryRemove(PlayerSession session, string itemId, int count, string reason)
        {
            if (session?.Save == null || session.Closed) return false;
            if (InventoryRules.TryRemove(session.Save.Inventory, itemId, count) != InventoryError.None) return false;
            Changed(session, itemId, -count, reason);
            return true;
        }

        /// <summary>
        /// Usa un oggetto (da PlayerInventory.UseItemRpc): controlla che esista, sia usabile e del giocatore,
        /// chiama il gestore e toglie un pezzo solo se il gestore riesce.
        /// </summary>
        public UseResult TryUse(PlayerSession session, string itemId, out string message)
        {
            message = null;
            if (session?.Save == null || session.Closed) { message = "You can't do that right now."; return UseResult.Failed; }
            if (!ItemCatalog.TryGet(itemId, out var item)) { message = "Unknown item."; return UseResult.UnknownItem; }
            if (!item.Usable) { message = $"{item.DisplayName} can't be used."; return UseResult.NotUsable; }
            if (Count(session, itemId) <= 0) { message = $"You don't have {item.DisplayName}."; return UseResult.NotOwned; }
            if (!ItemUseHandlers.TryGet(item, out var handler)) { message = $"{item.DisplayName} can't be used yet."; return UseResult.NoHandler; }

            bool ok;
            try { ok = handler.TryUse(session, item, out message); }
            catch (Exception e)
            {
                Debug.LogException(e);
                ok = false;
                message = "Something went wrong.";
            }
            if (!ok)
            {
                message ??= $"You can't use {item.DisplayName} right now.";
                return UseResult.Failed;
            }
            if (!TryRemove(session, itemId, 1, "use"))
            {
                // Non deve succedere (il conteggio è stato controllato sopra e il gestore non deve toglierlo).
                Debug.LogWarning($"[Inventory] {item.Id} usato da {session.Name} ma non tolto.");
            }
            message ??= $"Used {item.DisplayName}.";
            return UseResult.Used;
        }

        /// <summary>Manda l'inventario al proprietario. Lo chiama SessionService quando il personaggio entra.</summary>
        internal void SyncToClient(PlayerSession session)
        {
            var inv = session?.GetPlayerComponent<PlayerInventory>();
            if (inv != null) inv.ServerSetSlots(session.Save.Inventory);
        }

        void Changed(PlayerSession session, string itemId, int delta, string reason)
        {
            session.MarkDirty(important: true);
            Debug.Log($"[Inventory] {session.Name}: {(delta > 0 ? "+" : "")}{delta} {itemId} → {Count(session, itemId)} ({reason ?? "unspecified"}).");
            SyncToClient(session);
            try { ItemsChanged?.Invoke(session, itemId, delta, reason); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
