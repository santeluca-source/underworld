using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Underworld.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Underworld.Net
{
    /// <summary>
    /// Il server di un'area (-server). Lo crea GameRoot. Compiti:
    /// - avvio di Netcode su ListenAddress:Port e caricamento della scena dell'area;
    /// - approvazione delle connessioni (ConnectionApprovalRules + IConnectionValidator dei moduli);
    /// - registro dei giocatori e spawn del personaggio su un AreaAnchor di tipo Spawn;
    /// - guardiano del processo padre (-parentPid) e chiusura pulita (evento ServerStopping).
    /// Le righe di log iniziano con [Server]. Solo server.
    /// </summary>
    public sealed class ServerRuntime : MonoBehaviour
    {
        /// <summary>Il server di questo processo (null sui client).</summary>
        public static ServerRuntime Instance { get; private set; }

        /// <summary>L'area è caricata e il server accetta i giocatori.</summary>
        public static event Action ServerReady;

        /// <summary>
        /// Il server si sta chiudendo (processo padre chiuso, uscita, errore). Ultimo momento per salvare (Fase 5).
        /// Viene lanciato una sola volta, prima di scollegare i giocatori.
        /// </summary>
        public static event Action ServerStopping;

        public AreaId Area { get; private set; }
        public ushort Port { get; private set; }
        public string ListenAddress { get; private set; }
        public int MaxPlayers { get; private set; }
        public string DataDir { get; private set; }
        /// <summary>Cartella di server.ready e server.stop (-rundir, default DataDir): serve quando più server condividono DataDir.</summary>
        public string RunDir { get; private set; }
        public bool IsReady { get; private set; }
        public bool IsStopping { get; private set; }

        /// <summary>Rifiuti contati per codice (DisconnectReasons). Per i test e il log.</summary>
        public IReadOnlyDictionary<string, int> Rejections => _rejections;

        NetworkManager _nm;
        UnderworldTransport _transport;
        GameObject _playerPrefab;
        string _sceneName;
        readonly Dictionary<string, int> _rejections = new Dictionary<string, int>();
        readonly Dictionary<string, int> _spawnUses = new Dictionary<string, int>();
        readonly ApprovalContext _approval = new ApprovalContext();

        Process _parent;
        int _parentPid;
        float _nextWatchdogCheck;
        string _readyFile;
        string _stopFile;

        /// <summary>Quante volte un motivo di rifiuto è stato usato.</summary>
        public int RejectionCount(string code) => code != null && _rejections.TryGetValue(code, out int n) ? n : 0;

        internal void Begin(NetworkManager nm, UnderworldTransport transport, GameObject playerPrefab, LaunchArgs args)
        {
            Instance = this;
            _nm = nm;
            _transport = transport;
            _playerPrefab = playerPrefab;

            Area = args.Area;
            Port = args.Port;
            ListenAddress = args.ListenAddress;
            MaxPlayers = Mathf.Clamp(args.GetInt("maxplayers", NetConstants.DefaultMaxPlayers), 1, 64);
            DataDir = string.IsNullOrEmpty(args.DataDir) ? Path.Combine(Application.persistentDataPath, "ServerData") : args.DataDir;
            RunDir = string.IsNullOrEmpty(args.RunDir) ? DataDir : args.RunDir;
            _sceneName = AreaInfo.SceneName(Area);

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            Debug.Log($"{NetConstants.ServerLog} Avvio: area {Area}, ascolto {ListenAddress}:{Port}, massimo {MaxPlayers} giocatori, dati in {DataDir}" +
                      (RunDir != DataDir ? $", file del processo in {RunDir}" : "") + ".");

            try
            {
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(RunDir);
                _readyFile = Path.Combine(RunDir, NetConstants.ReadyFileName);
                _stopFile = Path.Combine(RunDir, NetConstants.StopFileName);
                // File rimasti da un avvio precedente: non devono ingannare chi aspetta.
                if (File.Exists(_readyFile)) File.Delete(_readyFile);
                if (File.Exists(_stopFile)) File.Delete(_stopFile);
            }
            catch (Exception e)
            {
                Debug.LogError($"{NetConstants.ServerLog} Cartella dati non valida: {DataDir} ({e.Message}).");
                _readyFile = null;
                _stopFile = null;
                StopServer("cartella dati non valida", 1);
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(_sceneName))
            {
                Debug.LogError($"{NetConstants.ServerLog} La scena {_sceneName} non è nella build.");
                StopServer("scena mancante", 1);
                return;
            }
            if (_playerPrefab == null)
            {
                Debug.LogError($"{NetConstants.ServerLog} Manca il prefab del giocatore in GameRoot.");
                StopServer("prefab mancante", 1);
                return;
            }

            StartParentWatchdog(args.ParentPid);

            _transport.SetConnectionData(true, ListenAddress, Port, ListenAddress);
            _nm.ConnectionApprovalCallback = Approve;
            _nm.OnClientDisconnectCallback += OnClientDisconnected;

            if (!_nm.StartServer())
            {
                Debug.LogError($"{NetConstants.ServerLog} Avvio di rete non riuscito su {ListenAddress}:{Port} (porta già usata?).");
                StopServer("avvio di rete non riuscito", 1);
                return;
            }

            _nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            _nm.SceneManager.OnSynchronizeComplete += OnClientSynchronized;

            var status = _nm.SceneManager.LoadScene(_sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"{NetConstants.ServerLog} Caricamento di {_sceneName} non partito: {status}.");
                StopServer("caricamento area non riuscito", 1);
            }
        }

        /// <summary>Area giocabile: mondo costruito (se l'area lo costruisce a codice) e almeno un punto di spawn.</summary>
        public static bool IsAreaPlayable(bool worldBuilt, int spawnCount) => worldBuilt && spawnCount > 0;

        void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (IsReady || sceneName != _sceneName) return;

            // Un'area costruita a codice deve avere il mondo e almeno un punto di spawn:
            // senza, i giocatori nascerebbero nel vuoto. Errore fatale, niente server.ready.
            var areaBuilder = Underworld.World.AreaBuilder.Current;
            int spawns = AreaAnchors.OfKind(AnchorKind.Spawn).Count;
            // Le aree fatte a mano (Area_TestGround) non hanno AreaBuilder: per loro conta solo lo spawn.
            bool worldBuilt = areaBuilder == null || areaBuilder.IsBuilt;
            if (!IsAreaPlayable(worldBuilt, spawns))
            {
                Debug.LogError($"{NetConstants.ServerLog} Costruzione dell'area {Area} fallita " +
                               $"(mondo costruito: {worldBuilt}, punti di spawn: {spawns}). Chiudo il server.");
                StopServer("costruzione area fallita", 1);
                return;
            }
            IsReady = true;

            ServerModules.Start(new ServerStartInfo
            {
                Area = Area,
                Port = Port,
                ListenAddress = ListenAddress,
                MaxPlayers = MaxPlayers,
                DataDir = DataDir,
                RunDir = RunDir,
                TestMode = LaunchArgs.Current.TestMode,
                Network = _nm,
            });

            WriteReadyFile();
            Debug.Log($"{NetConstants.ServerReadyMarker}: area {Area} ({_sceneName}) su {ListenAddress}:{Port}, punti di spawn: {spawns}.");
            try { ServerReady?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Scrive il file "pronto" (pid, area, porta) per chi aspetta il server.</summary>
        void WriteReadyFile()
        {
            if (string.IsNullOrEmpty(_readyFile)) return;
            try
            {
                int pid;
                using (var me = Process.GetCurrentProcess()) pid = me.Id;
                string json = "{\"pid\":" + pid + ",\"area\":\"" + Area + "\",\"port\":" + Port +
                              ",\"listen\":\"" + ListenAddress + "\"}";
                string tmp = _readyFile + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(_readyFile)) File.Delete(_readyFile);
                File.Move(tmp, _readyFile);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{NetConstants.ServerLog} File 'pronto' non scritto: {e.Message}");
            }
        }

        void DeleteReadyFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(_readyFile) && File.Exists(_readyFile)) File.Delete(_readyFile);
            }
            catch (Exception)
            {
                // Non importante: il prossimo avvio lo cancella comunque.
            }
        }

        // ---------------- Approvazione ----------------

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false; // lo spawn lo fa SpawnPlayer, dopo la sincronizzazione della scena
            response.Pending = false;

            ApprovalResult result;
            if (IsStopping) result = ApprovalResult.Reject(DisconnectReasons.ServerClosing);
            else if (!IsReady) result = ApprovalResult.Reject(DisconnectReasons.ServerStarting);
            else
            {
                _approval.MaxPlayers = MaxPlayers;
                _approval.CurrentPlayers = ServerPlayerRegistry.Count;
                _approval.IsPlayerIdInUse ??= id => ServerPlayerRegistry.TryGetByPlayerId(id, out _);
                _approval.Validators = ServerApproval.Validators;
                _approval.AllowReplace = ServerApproval.ReplacePolicy;
                try
                {
                    result = ConnectionApprovalRules.Evaluate(request.Payload, request.ClientNetworkId, _approval);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    result = ApprovalResult.Reject(DisconnectReasons.InternalError);
                }
            }

            response.Approved = result.Approved;
            response.Reason = result.Approved ? null : result.Reason;

            if (result.Approved)
            {
                ServerPlayerRegistry.TryGetByPlayerId(result.PlayerId, out var replaced);
                var entry = ServerPlayerRegistry.AddApproved(request.ClientNetworkId, result.PlayerId, result.Name, result.Ticket,
                    result.ReplacesExisting ? replaced : null);
                Debug.Log($"{NetConstants.ServerLog} Approvato: {entry} ({ServerPlayerRegistry.Count}/{MaxPlayers})" +
                          (result.ReplacesExisting && replaced != null ? $", sostituisce il client {replaced.ClientId}." : "."));
            }
            else
            {
                DisconnectReasons.TryParse(result.Reason, out string code, out _);
                code ??= "unknown";
                _rejections[code] = RejectionCount(code) + 1;
                Debug.Log($"{NetConstants.ServerLog} Rifiutato client {request.ClientNetworkId}: {result.Reason}");
            }
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId) return;
            var entry = ServerPlayerRegistry.Remove(clientId);
            if (entry != null)
                Debug.Log($"{NetConstants.ServerLog} Uscito: {entry} ({ServerPlayerRegistry.Count}/{MaxPlayers}).");
        }

        // ---------------- Spawn ----------------

        void OnClientSynchronized(ulong clientId)
        {
            if (!_nm.IsServer || IsStopping) return;
            if (!ServerPlayerRegistry.TryGetByClientId(clientId, out var entry) || entry.Joined) return;
            try
            {
                SpawnPlayer(entry);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _nm.DisconnectClient(clientId, DisconnectReasons.InternalError);
            }
        }

        void SpawnPlayer(ServerPlayerEntry entry)
        {
            ChooseSpawn(out Vector3 position, out Quaternion rotation);
            var go = Instantiate(_playerPrefab, position, rotation);
            var netObj = go.GetComponent<NetworkObject>();
            netObj.SpawnAsPlayerObject(entry.ClientId, true);
            ServerPlayerRegistry.MarkJoined(entry, netObj);
            Debug.Log($"{NetConstants.ServerLog} Entrato: {entry} in {position}. Giocatori in area: {ServerPlayerRegistry.JoinedCount}.");
        }

        /// <summary>Punto di spawn più libero tra gli AreaAnchor di tipo Spawn (i giocatori nascono sparsi).</summary>
        void ChooseSpawn(out Vector3 position, out Quaternion rotation)
        {
            var anchors = AreaAnchors.OfKind(AnchorKind.Spawn);
            var occupied = new List<Vector3>();
            foreach (var p in ServerPlayerRegistry.All)
                if (p.PlayerObject != null) occupied.Add(p.PlayerObject.transform.position);

            if (anchors.Count == 0)
            {
                Debug.LogWarning($"{NetConstants.ServerLog} Nessun punto di spawn nell'area: uso il centro.");
                position = Vector3.up * 1f + SpawnSelector.CrowdOffset(occupied.Count);
                rotation = Quaternion.identity;
                return;
            }

            var points = new List<Vector3>(anchors.Count);
            var uses = new List<int>(anchors.Count);
            foreach (var a in anchors)
            {
                points.Add(a.transform.position);
                uses.Add(_spawnUses.TryGetValue(a.Id ?? string.Empty, out int n) ? n : 0);
            }
            int index = SpawnSelector.Choose(points, occupied, uses);
            var chosen = anchors[index];
            string key = chosen.Id ?? string.Empty;
            int used = _spawnUses.TryGetValue(key, out int u) ? u : 0;
            _spawnUses[key] = used + 1;

            // Se anche il punto migliore è occupato, sposta un po' il giocatore (spirale).
            float nearest = float.PositiveInfinity;
            foreach (var o in occupied)
            {
                Vector3 d = o - chosen.transform.position;
                d.y = 0f;
                nearest = Mathf.Min(nearest, d.magnitude);
            }
            Vector3 offset = nearest < 1.5f ? SpawnSelector.CrowdOffset(used + 1) : Vector3.zero;
            position = chosen.transform.position + offset + Vector3.up * 0.05f;
            rotation = Quaternion.Euler(0f, chosen.transform.eulerAngles.y, 0f);
        }

        // ---------------- Guardiano del processo padre ----------------

        void StartParentWatchdog(int pid)
        {
            if (pid <= 0) return;
            _parentPid = pid;
            try
            {
                // Teniamo l'oggetto Process: così un pid riusato da un altro programma non ci inganna.
                _parent = Process.GetProcessById(pid);
                Debug.Log($"{NetConstants.ServerLog} Controllo il processo padre {pid}: se si chiude, mi chiudo anche io.");
            }
            catch (Exception)
            {
                Debug.LogWarning($"{NetConstants.ServerLog} Il processo padre {pid} non esiste più: chiudo il server.");
                StopServer("processo padre assente", 0);
            }
        }

        void Update()
        {
            if (IsStopping) return;
            if (Time.realtimeSinceStartup < _nextWatchdogCheck) return;
            _nextWatchdogCheck = Time.realtimeSinceStartup + 0.5f;

            if (_parent != null)
            {
                bool exited;
                try { exited = _parent.HasExited; }
                catch (Exception) { exited = true; }
                if (exited)
                {
                    Debug.Log($"{NetConstants.ServerLog} Il processo padre {_parentPid} si è chiuso: chiudo il server.");
                    StopServer("processo padre chiuso", 0);
                    return;
                }
            }

            // Richiesta di chiusura con un file (test automatici, launcher).
            if (!string.IsNullOrEmpty(_stopFile) && File.Exists(_stopFile))
            {
                try { File.Delete(_stopFile); }
                catch (Exception) { /* lo ignoro: chiudo comunque */ }
                Debug.Log($"{NetConstants.ServerLog} Richiesta di chiusura ricevuta ({NetConstants.StopFileName}).");
                StopServer("richiesta di chiusura", 0);
            }
        }

        // ---------------- Chiusura ----------------

        /// <summary>Chiude il server in modo pulito: evento ServerStopping, moduli, giocatori scollegati, uscita.</summary>
        public void StopServer(string why, int exitCode)
        {
            if (IsStopping) return;
            IsStopping = true;
            Debug.Log($"{NetConstants.ServerLog} Chiusura ({why}).");
            RaiseStopping();
            StartCoroutine(ShutdownAndQuit(exitCode));
        }

        void RaiseStopping()
        {
            try { ServerStopping?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            ServerModules.Stop();
        }

        IEnumerator ShutdownAndQuit(int exitCode)
        {
            DeleteReadyFile();
            // Test automatico del server: se non è finito, conta come fallito; un test fallito esce con 1.
            var runner = Underworld.Testing.AutoTestRunner.Instance;
            if (runner != null && !Underworld.Testing.AutoTestRunner.Finished)
                runner.AbortAndFinish("server chiuso prima della fine dello scenario");
            if (exitCode == 0 && Underworld.Testing.AutoTestRunner.Failed) exitCode = 1;
            if (_nm != null && _nm.IsListening)
            {
                var ids = new List<ulong>(_nm.ConnectedClientsIds);
                foreach (var id in ids)
                    if (id != NetworkManager.ServerClientId) _nm.DisconnectClient(id, DisconnectReasons.ServerClosing);
                // Lascia partire i messaggi di disconnessione.
                float until = Time.realtimeSinceStartup + 0.5f;
                while (Time.realtimeSinceStartup < until) yield return null;
                _nm.Shutdown();
                until = Time.realtimeSinceStartup + 2f;
                while ((_nm.IsListening || _nm.ShutdownInProgress) && Time.realtimeSinceStartup < until) yield return null;
            }
            Debug.Log($"{NetConstants.ServerLog} Server chiuso (codice {exitCode}).");
            Application.Quit(exitCode);
        }

        void OnApplicationQuit()
        {
            // Uscita senza StopServer (es. chiusura della finestra): l'evento parte lo stesso.
            if (IsStopping) return;
            IsStopping = true;
            Debug.Log($"{NetConstants.ServerLog} Chiusura (uscita dell'applicazione).");
            DeleteReadyFile();
            RaiseStopping();
        }

        void OnDestroy()
        {
            if (_nm != null)
            {
                _nm.OnClientDisconnectCallback -= OnClientDisconnected;
                if (_nm.SceneManager != null)
                {
                    _nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
                    _nm.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
                }
            }
            _parent?.Dispose();
            if (Instance == this) Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            ServerReady = null;
            ServerStopping = null;
        }
    }
}
