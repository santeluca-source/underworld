using System;
using System.Collections.Generic;
using Underworld.Core;
using Underworld.Net;
using Underworld.Server;
using Underworld.UI;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Underworld.Inventory
{
    /// <summary>Una riga dell'inventario mandata al proprietario.</summary>
    public struct InventorySlot : INetworkSerializable, IEquatable<InventorySlot>
    {
        public FixedString64Bytes ItemId;
        public int Count;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Count);
        }

        public bool Equals(InventorySlot other) => ItemId.Equals(other.ItemId) && Count == other.Count;
        public override bool Equals(object obj) => obj is InventorySlot o && Equals(o);
        public override int GetHashCode() => ItemId.GetHashCode() * 31 + Count;
    }

    /// <summary>
    /// Inventario sul personaggio (prefab Player, aggiunto da InventoryPlayerContributor).
    /// - NetworkList leggibile solo dal proprietario, scritta solo dal server (InventoryService).
    /// - UseItemRpc(id): il client chiede di usare un oggetto; il server controlla mittente, frequenza,
    ///   possesso e gestore (InventoryService.TryUse) e risponde con UseResultRpc.
    /// - Tasto I (proprietario): apre e chiude la finestra Inventory.
    /// </summary>
    public sealed class PlayerInventory : NetworkBehaviour
    {
        /// <summary>Usi al secondo per client (con una piccola riserva).</summary>
        static readonly RateLimiter UseLimiter = new RateLimiter(3f, 5f);

        NetworkList<InventorySlot> _slots;

        /// <summary>L'inventario del giocatore locale (null sul server o prima dello spawn).</summary>
        public static PlayerInventory Local { get; private set; }

        /// <summary>L'inventario locale è cambiato.</summary>
        public static event Action LocalChanged;

        /// <summary>Risposta del server a un uso: riuscito, id, messaggio (inglese).</summary>
        public static event Action<bool, string, string> LocalUseResult;

        /// <summary>Usi rifiutati dal server su questo processo (test).</summary>
        public static int ServerRejectedUses { get; private set; }

        void Awake()
        {
            _slots = new NetworkList<InventorySlot>(null, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner || IsServer) return;
            Local = this;
            _slots.OnListChanged += OnListChanged;
            LocalChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            if (_slots != null) _slots.OnListChanged -= OnListChanged;
            if (Local == this)
            {
                Local = null;
                if (UiWindows.IsOpen(InventoryWindow.Id)) UiWindows.Close(InventoryWindow.Id);
            }
            if (IsServer) UseLimiter.RemoveClient(OwnerClientId);
        }

        void OnListChanged(NetworkListEvent<InventorySlot> e) => LocalChanged?.Invoke();

        // ---------------- Lettura (proprietario o server) ----------------

        public int SlotCount => _slots?.Count ?? 0;

        public InventorySlot GetSlot(int index) => _slots[index];

        public int CountOf(string itemId)
        {
            if (_slots == null || itemId == null) return 0;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].ItemId.ToString() == itemId) return _slots[i].Count;
            return 0;
        }

        /// <summary>Copia delle righe (per la UI).</summary>
        public List<InventorySlot> Snapshot()
        {
            var list = new List<InventorySlot>(SlotCount);
            for (int i = 0; i < SlotCount; i++) list.Add(_slots[i]);
            return list;
        }

        // ---------------- Tasto I ----------------

        void Update()
        {
            if (!IsOwner || IsServer) return;
            var kb = Keyboard.current;
            if (kb == null || !kb.iKey.wasPressedThisFrame) return;
            if (UiWindows.IsOpen(InventoryWindow.Id)) UiWindows.Close(InventoryWindow.Id);
            else if (!InputGate.GameplayBlocked) UiWindows.Open(InventoryWindow.Id);
        }

        // ---------------- Uso ----------------

        /// <summary>Proprietario: chiede al server di usare un oggetto. Il risultato arriva con LocalUseResult.</summary>
        public void RequestUse(string itemId)
        {
            if (!IsOwner || !IsSpawned || !ItemCatalog.IsValidId(itemId)) return;
            UseItemRpc(new FixedString64Bytes(itemId));
        }

        [Rpc(SendTo.Server)]
        void UseItemRpc(FixedString64Bytes itemId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            string id = itemId.ToString();
            if (sender != OwnerClientId)
            {
                ServerRejectedUses++;
                Debug.LogWarning($"{NetConstants.ServerLog} UseItem rifiutato: il client {sender} non possiede questo personaggio.");
                return;
            }
            if (!UseLimiter.TryConsume(sender, "inventory.use"))
            {
                ServerRejectedUses++;
                UseResultRpc(false, itemId, new FixedString128Bytes("Slow down."));
                return;
            }
            if (!ServerContext.TryGetSession(sender, out var session) || !ItemCatalog.IsValidId(id))
            {
                ServerRejectedUses++;
                UseResultRpc(false, itemId, new FixedString128Bytes("You can't do that right now."));
                return;
            }
            var result = ServerContext.Current.Inventory.TryUse(session, id, out string message);
            if (result != UseResult.Used) ServerRejectedUses++;
            UseResultRpc(result == UseResult.Used, itemId, new FixedString128Bytes(Trim(message, 120)));
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void UseResultRpc(bool ok, FixedString64Bytes itemId, FixedString128Bytes message, RpcParams rpcParams = default)
        {
            if (!IsOwner || rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            string text = message.ToString();
            if (!string.IsNullOrEmpty(text)) HudBus.Toast(text, ok ? ToastKind.Success : ToastKind.Warning);
            LocalUseResult?.Invoke(ok, itemId.ToString(), text);
        }

        // ---------------- Server ----------------

        /// <summary>Solo server (InventoryService): riscrive la lista in ordine di id.</summary>
        internal void ServerSetSlots(IReadOnlyDictionary<string, int> items)
        {
            if (!IsServer || !IsSpawned || _slots == null) return;
            var ids = new List<string>(items?.Keys ?? (IEnumerable<string>)Array.Empty<string>());
            ids.Sort(StringComparer.Ordinal);
            // Aggiorna solo le righe diverse: meno traffico per il cambio di un solo oggetto.
            int i = 0;
            for (; i < ids.Count; i++)
            {
                var slot = new InventorySlot { ItemId = new FixedString64Bytes(ids[i]), Count = items[ids[i]] };
                if (i < _slots.Count)
                {
                    if (!_slots[i].Equals(slot)) _slots[i] = slot;
                }
                else _slots.Add(slot);
            }
            while (_slots.Count > ids.Count) _slots.RemoveAt(_slots.Count - 1);
        }

        static string Trim(string s, int max) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length > max ? s.Substring(0, max) : s);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Local = null;
            LocalChanged = null;
            LocalUseResult = null;
            ServerRejectedUses = 0;
            UseLimiter.Clear();
        }
    }
}
