using System.Collections.Generic;
using Newtonsoft.Json;
using Underworld.Inventory;
using Underworld.Net;
using UnityEngine;

namespace Underworld.Server
{
    /// <summary>
    /// Comandi di test del server (solo scenari automatici). Attivi SOLO se il server è partito con -testmode:
    /// ServerCoreModule chiama Enable solo in quel caso. Senza -testmode il gestore non esiste
    /// e ogni richiesta (PlayerTestHooks.TestCommandRpc) viene rifiutata e contata in RejectedCount.
    /// Il launcher (Play, Play with friends) non passa mai -testmode: nelle partite vere sono irraggiungibili
    /// (scenario testhooks_disabled).
    /// Comandi: set_cash &lt;valore&gt;, give_item &lt;id&gt; &lt;quantità&gt;, state (JSON con soldi e inventario).
    /// </summary>
    public static class ServerTestHooks
    {
        static ServerContext _ctx;

        /// <summary>Vero solo su un server con -testmode.</summary>
        public static bool Enabled => _ctx != null;

        /// <summary>Richieste rifiutate perché i comandi di test sono spenti.</summary>
        public static int RejectedCount { get; private set; }

        /// <summary>Comandi eseguiti (con -testmode).</summary>
        public static int ExecutedCount { get; private set; }

        internal static void Enable(ServerContext ctx)
        {
            if (ctx == null || !ctx.TestMode) return; // seconda difesa: mai senza -testmode
            _ctx = ctx;
            TestItems.Register();
            Debug.LogWarning($"{NetConstants.ServerLog} COMANDI DI TEST ATTIVI (-testmode): solo per gli scenari automatici.");
        }

        /// <summary>Esegue un comando per il giocatore del client. Falso (e "disabled") senza -testmode.</summary>
        public static bool TryExecute(ulong clientId, string command, string arg, long value, out string result)
        {
            if (_ctx == null)
            {
                RejectedCount++;
                Debug.LogWarning($"{NetConstants.ServerLog} Comando di test '{command}' dal client {clientId} rifiutato: server senza -testmode.");
                result = "disabled";
                return false;
            }
            if (!_ctx.Sessions.TryGet(clientId, out var session))
            {
                result = "no_session";
                return false;
            }
            bool ok;
            switch (command)
            {
                case "set_cash":
                    ok = SetCash(session, value);
                    result = ok ? "ok" : "invalid";
                    break;
                case "give_item":
                    ok = GiveItem(session, arg, (int)Mathf.Clamp(value, 1, 1000), out var error);
                    result = ok ? "ok" : error.ToString();
                    break;
                case "state":
                    ok = true;
                    result = ReadState(session);
                    break;
                default:
                    ok = false;
                    result = "unknown_command";
                    break;
            }
            if (ok) ExecutedCount++;
            Debug.Log($"{NetConstants.ServerLog} Comando di test {command} {arg} {value} per {session.Name}: {result}.");
            return ok;
        }

        // ---- API per gli scenari del server (stesso processo) ----

        public static bool SetCash(PlayerSession session, long value) =>
            Enabled && _ctx.Economy.SetCashForTests(session, value, "test.set_cash");

        public static bool GiveItem(PlayerSession session, string itemId, int count, out InventoryError error)
        {
            error = InventoryError.UnknownItem;
            if (!Enabled) return false;
            return _ctx.Inventory.TryAdd(session, itemId, count, "test.give_item", out error);
        }

        /// <summary>Stato del giocatore in JSON: {"cash":..,"inventory":{..},"saves":..}.</summary>
        public static string ReadState(PlayerSession session)
        {
            if (!Enabled || session == null) return "{}";
            var state = new
            {
                cash = session.Save.Cash,
                inventory = new SortedDictionary<string, int>(session.Save.Inventory),
                saves = session.SaveCount,
            };
            return JsonConvert.SerializeObject(state);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _ctx = null;
            RejectedCount = 0;
            ExecutedCount = 0;
        }
    }

    /// <summary>
    /// Oggetti finti usati solo dagli scenari: li registra ServerTestHooks.Enable (server con -testmode).
    /// I moduli veri registrano i loro oggetti; questi non esistono nelle partite vere.
    /// </summary>
    public static class TestItems
    {
        public const string Snack = "test.snack";
        public const string Rock = "test.rock";

        public static void Register()
        {
            ItemCatalog.TryRegister(new ItemDefinition
            {
                Id = Snack, DisplayName = "Test Snack", Description = "A test item you can use.",
                Price = 10, MaxStack = 10, Category = ItemCategory.Consumable, Usable = true,
            });
            ItemCatalog.TryRegister(new ItemDefinition
            {
                Id = Rock, DisplayName = "Test Rock", Description = "A test item you can't use.",
                Price = 0, MaxStack = 5, Category = ItemCategory.Misc, Usable = false,
            });
            ItemUseHandlers.Register(Snack, new SnackHandler());
        }

        sealed class SnackHandler : IItemUseHandler
        {
            public bool TryUse(PlayerSession session, ItemDefinition item, out string message)
            {
                message = "You ate the test snack.";
                return true;
            }
        }
    }
}
