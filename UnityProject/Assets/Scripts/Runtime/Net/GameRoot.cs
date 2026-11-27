using System;
using System.Collections;
using Underworld.Core;
using Underworld.Testing;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Underworld.Net
{
    /// <summary>
    /// Radice del gioco nella scena Bootstrap (indice 0 della build). Resta viva per tutta la partita
    /// (DontDestroyOnLoad) ed esiste una sola volta, anche se la scena Bootstrap viene ricaricata.
    /// Sullo stesso oggetto: NetworkManager, UnderworldTransport, GameLauncher.
    /// All'avvio legge LaunchArgs.Current e decide:
    /// - "-server": avvia il server dell'area (ServerRuntime);
    /// - "-connect ip:porta": si collega subito, senza menu;
    /// - altrimenti carica la scena MainMenu (se c'è nella build).
    /// Con "-autotest" avvia anche AutoTestRunner.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class GameRoot : MonoBehaviour
    {
        /// <summary>L'unica radice del gioco (null prima dell'avvio).</summary>
        public static GameRoot Instance { get; private set; }

        [Tooltip("Prefab del giocatore (Assets/Prefabs/Player.prefab). Lo crea il server per ogni client.")]
        public GameObject playerPrefab;

        [Tooltip("Lista unica dei prefab di rete (Assets/DefaultNetworkPrefabs.asset).")]
        public NetworkPrefabsList networkPrefabs;

        public NetworkManager Network { get; private set; }
        public UnderworldTransport Transport { get; private set; }
        public GameLauncher Launcher { get; private set; }
        /// <summary>Il server di questo processo (null sui client).</summary>
        public ServerRuntime Server { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Seconda copia (Bootstrap ricaricata): la spengo prima che Netcode si avvii.
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            ConfigureNetwork();

            Launcher = GetComponent<GameLauncher>();
            if (Launcher == null) Launcher = gameObject.AddComponent<GameLauncher>();
            Launcher.Init(Network, Transport);
            GameServices.Launcher = Launcher;
        }

        /// <summary>Crea (se mancano) e configura NetworkManager e UnderworldTransport.</summary>
        void ConfigureNetwork()
        {
            Transport = GetComponent<UnderworldTransport>();
            if (Transport == null) Transport = gameObject.AddComponent<UnderworldTransport>();
            Network = GetComponent<NetworkManager>();
            if (Network == null) Network = gameObject.AddComponent<NetworkManager>();

            var cfg = Network.NetworkConfig ?? (Network.NetworkConfig = new NetworkConfig());
            cfg.NetworkTransport = Transport;
            cfg.TickRate = NetConstants.TickRate;
            cfg.ConnectionApproval = true;
            cfg.EnableSceneManagement = true;
            cfg.PlayerPrefab = null; // il personaggio lo crea ServerRuntime, sul punto di spawn
            cfg.ForceSamePrefabs = true;
            if (networkPrefabs != null && !cfg.Prefabs.NetworkPrefabsLists.Contains(networkPrefabs))
                cfg.Prefabs.NetworkPrefabsLists.Add(networkPrefabs);
            if (networkPrefabs == null)
                Debug.LogWarning("[GameRoot] Manca la lista dei prefab di rete: rifai il setup del progetto.");
        }

        IEnumerator Start()
        {
            if (Instance != this) yield break;
            // Un frame di attesa: le altre parti della scena finiscono il loro Awake/Start.
            yield return null;

            var args = LaunchArgs.Current;
            if (!string.IsNullOrEmpty(args.AutoTest)) AutoTestRunner.StartIfRequested(this);

            if (args.IsServer)
            {
                Server = gameObject.AddComponent<ServerRuntime>();
                Server.Begin(Network, Transport, playerPrefab, args);
                yield break;
            }

            // Client senza grafica (bot, test): non serve andare più veloce di 60 FPS.
            if (Application.isBatchMode)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
            }

            if (!string.IsNullOrEmpty(args.ConnectAddress))
            {
                if (TryParseAddress(args.ConnectAddress, out string ip, out ushort port))
                {
                    Launcher.Join(ip, port, string.IsNullOrEmpty(args.PlayerName) ? GameLauncher.DefaultPlayerName : args.PlayerName);
                    yield break;
                }
                Debug.LogWarning($"{NetConstants.ClientLog} Indirizzo -connect non valido: '{args.ConnectAddress}' (formato ip:porta).");
                Launcher.ReportFailure($"Invalid address: {args.ConnectAddress}. Use the format ip:port.");
            }

            LoadMainMenuIfPresent();
        }

        /// <summary>Carica la scena MainMenu se è nella build; altrimenti scrive un avviso e resta dove è.</summary>
        public static bool LoadMainMenuIfPresent()
        {
            if (SceneManager.GetActiveScene().name == NetConstants.MainMenuScene) return true;
            if (!Application.CanStreamedLevelBeLoaded(NetConstants.MainMenuScene))
            {
                Debug.LogWarning($"[GameRoot] La scena {NetConstants.MainMenuScene} non è nella build: resto qui.");
                return false;
            }
            SceneManager.LoadScene(NetConstants.MainMenuScene, LoadSceneMode.Single);
            return true;
        }

        /// <summary>Legge "ip:porta". Falso se il formato non va bene.</summary>
        public static bool TryParseAddress(string text, out string ip, out ushort port)
        {
            ip = null;
            port = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            int i = text.LastIndexOf(':');
            if (i <= 0 || i >= text.Length - 1) return false;
            ip = text.Substring(0, i);
            return ushort.TryParse(text.Substring(i + 1), out port) && port != 0;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (ReferenceEquals(GameServices.Launcher, Launcher)) GameServices.Launcher = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;
    }
}
