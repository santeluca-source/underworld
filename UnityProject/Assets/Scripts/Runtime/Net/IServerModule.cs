using System;
using System.Collections.Generic;
using Underworld.Core;
using Unity.Netcode;
using UnityEngine;

namespace Underworld.Net
{
    /// <summary>Dati del server che ricevono i moduli all'avvio.</summary>
    public sealed class ServerContext
    {
        public AreaId Area;
        public ushort Port;
        public string ListenAddress;
        public int MaxPlayers;
        /// <summary>Cartella dati del server (-datadir), già creata.</summary>
        public string DataDir;
        public NetworkManager Network;
    }

    /// <summary>
    /// Servizio del server (contratto, sezione 13). Ogni classe non astratta con costruttore vuoto
    /// che implementa questa interfaccia viene creata da sola all'avvio del server.
    /// Ordine delle chiamate: OnServerStarted (area pronta) → OnPlayerJoined/OnPlayerLeft → OnServerStopping.
    /// Esempi futuri: salvataggi (Fase 5), economia (Fase 6).
    /// </summary>
    public interface IServerModule
    {
        /// <summary>Ordine di avvio: più basso = prima.</summary>
        int Order { get; }
        void OnServerStarted(ServerContext context);
        void OnPlayerJoined(ServerPlayerEntry player);
        void OnPlayerLeft(ServerPlayerEntry player);
        /// <summary>Il server si chiude: ultimo momento per salvare.</summary>
        void OnServerStopping();
    }

    /// <summary>Trova e chiama gli IServerModule. Solo server.</summary>
    public static class ServerModules
    {
        static readonly List<IServerModule> Modules = new List<IServerModule>();
        static bool _started;

        public static IReadOnlyList<IServerModule> All => Modules;

        internal static void Start(ServerContext context)
        {
            if (_started) return;
            _started = true;
            Modules.Clear();
            try
            {
                foreach (var type in typeof(IServerModule).Assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface || !typeof(IServerModule).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                    Modules.Add((IServerModule)Activator.CreateInstance(type));
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Modules.Sort((a, b) => a.Order.CompareTo(b.Order));

            foreach (var m in Modules)
            {
                try { m.OnServerStarted(context); }
                catch (Exception e) { Debug.LogException(e); }
            }
            Debug.Log($"{NetConstants.ServerLog} Moduli del server: {Modules.Count}.");

            ServerPlayerRegistry.PlayerJoined += OnJoined;
            ServerPlayerRegistry.PlayerLeft += OnLeft;
        }

        static void OnJoined(ServerPlayerEntry p)
        {
            foreach (var m in Modules)
            {
                try { m.OnPlayerJoined(p); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static void OnLeft(ServerPlayerEntry p)
        {
            foreach (var m in Modules)
            {
                try { m.OnPlayerLeft(p); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        internal static void Stop()
        {
            if (!_started) return;
            for (int i = Modules.Count - 1; i >= 0; i--)
            {
                try { Modules[i].OnServerStopping(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            ServerPlayerRegistry.PlayerJoined -= OnJoined;
            ServerPlayerRegistry.PlayerLeft -= OnLeft;
            _started = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Modules.Clear();
            _started = false;
        }
    }
}
