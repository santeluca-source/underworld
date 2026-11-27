using System;
using System.Diagnostics;
using Underworld.Core;
using Underworld.Economy;
using Underworld.Inventory;
using Underworld.Net;
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Underworld.Server
{
    /// <summary>
    /// I servizi del server di questo processo (solo server). Lo crea ServerCoreModule all'avvio dell'area.
    /// Uso tipico in un modulo: <c>var ctx = ServerContext.Current; ctx.Economy.TryDebit(session, 500, "shop.burger");</c>
    /// Sui client Current è sempre null.
    /// </summary>
    public sealed class ServerContext
    {
        /// <summary>Il contesto del server, o null (client, server non ancora pronto).</summary>
        public static ServerContext Current { get; private set; }

        public ServerStartInfo Info { get; }
        public AreaId Area => Info.Area;
        public string DataDir => Info.DataDir;
        public NetworkManager Network => Info.Network;
        public bool TestMode => Info.TestMode;
        public int Pid { get; }

        public IPlayerStore Store { get; }
        public SessionLockStore Locks { get; }
        public SessionService Sessions { get; }
        public TransactionJournal Journal { get; }
        public EconomyService Economy { get; }
        public InventoryService Inventory { get; }

        ServerContext(ServerStartInfo info, IPlayerStore store)
        {
            Info = info;
            using (var me = Process.GetCurrentProcess()) Pid = me.Id;
            Store = store;
            Locks = new SessionLockStore(info.DataDir, info.Area.ToString(), Pid, info.Port);
            Journal = new TransactionJournal(info.DataDir, info.Area.ToString());
            Economy = new EconomyService(Journal);
            Inventory = new InventoryService();
            Sessions = new SessionService(this, store, Locks);
            Sessions.SessionJoined += Economy.SyncToClient;
            Sessions.SessionJoined += Inventory.SyncToClient;
        }

        /// <summary>Sessione di un client, o null. Comodo nelle RPC: <c>ServerContext.TryGetSession(senderId, out var s)</c>.</summary>
        public static bool TryGetSession(ulong clientId, out PlayerSession session)
        {
            session = null;
            return Current != null && Current.Sessions.TryGet(clientId, out session);
        }

        internal static ServerContext Create(ServerStartInfo info)
        {
            Current = new ServerContext(info, new JsonFilePlayerStore(info.DataDir, Money.MaxCash));
            return Current;
        }

        internal static void Clear() => Current = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;
    }

    /// <summary>
    /// Modulo base del server (Fase 5–6): crea ServerContext, registra il validatore dell'identità e la regola
    /// "la sessione più nuova vince", collega gli eventi dei giocatori e salva tutto a ServerStopping.
    /// Ordine -1000: parte prima degli altri moduli, che quindi trovano già ServerContext.Current.
    /// </summary>
    public sealed class ServerCoreModule : IServerModule
    {
        public int Order => -1000;

        ServerContext _ctx;
        GameObject _pump;

        public void OnServerStarted(ServerStartInfo info)
        {
            _ctx = ServerContext.Create(info);
            ServerApproval.AddValidator(_ctx.Sessions);
            ServerApproval.ReplacePolicy = _ctx.Sessions.AllowReplace;
            ServerPlayerRegistry.PlayerApproved += _ctx.Sessions.OnApproved;
            ServerPlayerRegistry.PlayerRemoved += _ctx.Sessions.OnRemoved;
            ServerRuntime.ServerStopping += OnStopping;

            _pump = new GameObject("ServerServices");
            UnityEngine.Object.DontDestroyOnLoad(_pump);
            _pump.AddComponent<ServerPump>().Context = _ctx;

            if (info.TestMode) ServerTestHooks.Enable(_ctx);

            Debug.Log($"{NetConstants.ServerLog} Servizi pronti: salvataggi in {info.DataDir}, " +
                      $"comandi di test {(ServerTestHooks.Enabled ? "ATTIVI (-testmode)" : "spenti")}.");
        }

        public void OnPlayerJoined(ServerPlayerEntry player) => _ctx?.Sessions.OnJoined(player);

        public void OnPlayerLeft(ServerPlayerEntry player)
        {
            // Il salvataggio finale lo fa PlayerRemoved (vale anche per chi esce prima di entrare nell'area).
        }

        void OnStopping()
        {
            _ctx?.Sessions.SaveAllAndRelease("chiusura del server");
        }

        public void OnServerStopping()
        {
            if (_ctx == null) return;
            _ctx.Sessions.SaveAllAndRelease("chiusura del server");
            ServerApproval.RemoveValidator(_ctx.Sessions);
            ServerApproval.ReplacePolicy = null;
            ServerPlayerRegistry.PlayerApproved -= _ctx.Sessions.OnApproved;
            ServerPlayerRegistry.PlayerRemoved -= _ctx.Sessions.OnRemoved;
            ServerRuntime.ServerStopping -= OnStopping;
            // ServerContext.Current resta: i comandi in volo dopo la chiusura trovano le sessioni chiuse.
        }
    }

    /// <summary>Chiama SessionService.Tick ogni frame (salvataggi in ritardo, periodici, battiti). Solo server.</summary>
    internal sealed class ServerPump : MonoBehaviour
    {
        internal ServerContext Context;

        void Update()
        {
            try { Context?.Sessions.Tick(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
