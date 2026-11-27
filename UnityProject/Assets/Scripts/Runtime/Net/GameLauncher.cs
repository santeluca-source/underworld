using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Underworld.Core;
using Underworld.Player;
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Underworld.Net
{
    /// <summary>
    /// Avvio e connessione del client (IGameLauncher, registrato in GameServices.Launcher da GameRoot).
    /// - PlayLocal: avvia un server figlio (stesso exe, -server) solo per questo PC e si collega.
    /// - HostForFriends: come PlayLocal, ma il server ascolta su tutta la rete di casa (0.0.0.0).
    /// - Join: si collega a un server esistente.
    /// - Disconnect: esce dalla partita e torna al menu. QuitGame: chiude il gioco.
    /// I server figli si chiudono da soli quando questo processo esce (-parentPid).
    /// Argomenti per i test: -localport, -localdatadir, -localarea.
    /// </summary>
    public sealed class GameLauncher : MonoBehaviour, IGameLauncher
    {
        /// <summary>Nome usato quando il giocatore non ne ha scelto uno.</summary>
        public const string DefaultPlayerName = "Giocatore";

        /// <summary>Tempo massimo per avviare il server locale e collegarsi (s).</summary>
        public const float LocalConnectTimeout = 25f;
        /// <summary>Tempo massimo per collegarsi a un server esistente (s).</summary>
        public const float JoinTimeout = 15f;
        /// <summary>Tempo massimo tra connessione e personaggio pronto (s).</summary>
        public const float SpawnTimeout = 30f;

        public LauncherState State { get; private set; } = LauncherState.Idle;
        public string StatusText { get; private set; } = string.Empty;
        public event Action StateChanged;

        /// <summary>Codice dell'ultimo motivo di rifiuto o disconnessione (DisconnectReasons), o null.</summary>
        public string LastReasonCode { get; private set; }
        /// <summary>Testo in italiano dell'ultimo motivo di rifiuto o disconnessione, o null.</summary>
        public string LastReasonText { get; private set; }

        /// <summary>Pid del server figlio avviato da PlayLocal/HostForFriends (0 se non c'è).</summary>
        public int ChildServerPid
        {
            get
            {
                if (_child == null) return 0;
                try { return _child.HasExited ? 0 : _child.Id; }
                catch (Exception) { return 0; }
            }
        }

        /// <summary>Cartella dati del server figlio (null se non c'è).</summary>
        public string ChildServerDataDir { get; private set; }

        NetworkManager _nm;
        UnderworldTransport _transport;
        Coroutine _flow;
        Process _child;
        bool _childForFriends;
        ushort _childPort;

        // Stato della connessione in corso.
        bool _clientRunning;
        bool _clientStoppedSignal;
        string _rawReason;
        bool _userDisconnect;
        string _inGameStatus;

        internal void Init(NetworkManager nm, UnderworldTransport transport)
        {
            _nm = nm;
            _transport = transport;
            _nm.OnClientDisconnectCallback += OnClientDisconnect;
            _nm.OnClientStopped += OnClientStopped;
            PlayerNetwork.LocalPlayerSpawned += OnLocalPlayerSpawned;
        }

        void OnDestroy()
        {
            if (_nm != null)
            {
                _nm.OnClientDisconnectCallback -= OnClientDisconnect;
                _nm.OnClientStopped -= OnClientStopped;
            }
            PlayerNetwork.LocalPlayerSpawned -= OnLocalPlayerSpawned;
            _child?.Dispose();
        }

        // ---------------- API (IGameLauncher) ----------------

        public void PlayLocal(string playerName) => StartLocal(playerName, false);

        public void HostForFriends(string playerName) => StartLocal(playerName, true);

        public void Join(string address, ushort port, string playerName)
        {
            if (!CanStart()) return;
            _flow = StartCoroutine(JoinFlow(address, port, CleanName(playerName)));
        }

        public void Disconnect()
        {
            StopFlow();
            _userDisconnect = true;
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
            _clientRunning = false;
            SetState(LauncherState.Idle, "Sei uscito dalla partita.");
            GameRoot.LoadMainMenuIfPresent();
        }

        public void QuitGame()
        {
            ShutdownForExit();
            Debug.Log($"{NetConstants.ClientLog} Chiusura del gioco.");
            // I server figli vedono che questo processo è chiuso (-parentPid) e si chiudono da soli.
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        /// <summary>
        /// Ferma la connessione senza cambiare scena né stato (prima di chiudere il processo:
        /// QuitGame e i test automatici).
        /// </summary>
        public void ShutdownForExit()
        {
            StopFlow();
            _userDisconnect = true;
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
        }

        // ---------------- Flussi ----------------

        bool CanStart()
        {
            if (_nm == null)
            {
                Debug.LogError($"{NetConstants.ClientLog} GameLauncher senza NetworkManager.");
                return false;
            }
            if (State == LauncherState.StartingServer || State == LauncherState.Connecting || State == LauncherState.InGame)
            {
                Debug.LogWarning($"{NetConstants.ClientLog} Avvio ignorato: c'è già una partita in corso ({State}).");
                return false;
            }
            if (_nm.IsListening || _nm.ShutdownInProgress)
            {
                Debug.LogWarning($"{NetConstants.ClientLog} Avvio ignorato: la rete non è ancora ferma.");
                return false;
            }
            return true;
        }

        void StartLocal(string playerName, bool forFriends)
        {
            if (!CanStart()) return;
            _flow = StartCoroutine(LocalFlow(CleanName(playerName), forFriends));
        }

        static string CleanName(string name) => string.IsNullOrWhiteSpace(name) ? DefaultPlayerName : name;

        IEnumerator JoinFlow(string address, ushort port, string playerName)
        {
            _inGameStatus = $"In gioco su {address}:{port}.";
            yield return ConnectWithRetry(address, port, playerName, Time.realtimeSinceStartup + JoinTimeout, null);
            _flow = null;
        }

        IEnumerator LocalFlow(string playerName, bool forFriends)
        {
            var args = LaunchArgs.Current;
            AreaId area = AreaId.Downtown;
            string areaArg = args.Get("localarea");
            if (!string.IsNullOrEmpty(areaArg) && !Enum.TryParse(areaArg, true, out area))
            {
                Fail($"Area sconosciuta: {areaArg}.");
                yield break;
            }
            ushort port = (ushort)Mathf.Clamp(args.GetInt("localport", AreaInfo.DefaultPort(area)), 1, 65535);
            string dataDir = args.Get("localdatadir");
            if (string.IsNullOrEmpty(dataDir)) dataDir = Path.Combine(Application.persistentDataPath, "LocalServer", area.ToString());
            float deadline = Time.realtimeSinceStartup + LocalConnectTimeout;

            // Un server figlio già acceso con le stesse impostazioni si riusa (es. dopo "Esci dalla partita").
            bool reuse = ChildServerPid != 0 && _childForFriends == forFriends && _childPort == port;
            if (!reuse)
            {
                yield return StopChildServer();
                SetState(LauncherState.StartingServer, "Avvio del server sul tuo PC...");
                if (!TryStartChildServer(area, port, dataDir, forFriends, out string error))
                {
                    Fail(error);
                    yield break;
                }
                // Aspetta il file "pronto" del server (se non arriva, prova a collegarti lo stesso).
                string readyFile = Path.Combine(dataDir, NetConstants.ReadyFileName);
                while (Time.realtimeSinceStartup < deadline && !IsReadyFileFor(readyFile, _child))
                {
                    if (ChildServerPid == 0)
                    {
                        Fail($"Il server sul tuo PC si è chiuso subito. Guarda il log: {ChildLogPath(dataDir)}");
                        yield break;
                    }
                    yield return new WaitForSecondsRealtime(0.25f);
                }
            }

            string lan = forFriends ? LocalNetworkInfo.GetLanIPv4() : null;
            _inGameStatus = forFriends
                ? (lan != null
                    ? $"Server aperto agli amici: {lan}:{port}. Apri la porta {port} UDP sul router per chi gioca fuori casa."
                    : $"Server aperto agli amici sulla porta {port}, ma non trovo l'indirizzo di rete di questo PC.")
                : $"In gioco sul tuo PC: {AreaInfo.DisplayName(area)}.";

            yield return ConnectWithRetry("127.0.0.1", port, playerName, deadline, () => ChildServerPid != 0);
            _flow = null;
        }

        /// <summary>
        /// Si collega e aspetta il personaggio. Riprova con attese crescenti se il server non risponde
        /// o si sta avviando; si ferma subito con un rifiuto vero (nome, server pieno...).
        /// </summary>
        IEnumerator ConnectWithRetry(string address, ushort port, string playerName, float deadline, Func<bool> serverAlive)
        {
            float wait = 0.5f;
            int attempt = 0;
            while (true)
            {
                attempt++;
                SetState(LauncherState.Connecting, attempt == 1
                    ? $"Collegamento a {address}:{port}..."
                    : $"Collegamento a {address}:{port}... (tentativo {attempt})");

                if (!StartClient(address, port, playerName, deadline))
                {
                    Fail("Impossibile avviare la connessione. Controlla indirizzo e porta.");
                    yield break;
                }

                // Aspetta: personaggio pronto (in gioco) oppure client fermo (rifiuto, errore, timeout).
                float spawnDeadline = Time.realtimeSinceStartup + SpawnTimeout;
                while (!_clientStoppedSignal && State != LauncherState.InGame)
                {
                    if (Time.realtimeSinceStartup > spawnDeadline)
                    {
                        _nm.Shutdown();
                        Fail("Il server non ha creato il personaggio in tempo.");
                        yield break;
                    }
                    yield return null;
                }
                if (State == LauncherState.InGame) yield break;

                // Il client si è fermato: decidi se riprovare.
                ParseReason(_rawReason, out string code, out string text);
                bool retryable = code == null || code == DisconnectReasons.ServerStartingCode;
                bool alive = serverAlive == null || serverAlive();
                if (!retryable || !alive || Time.realtimeSinceStartup + wait > deadline)
                {
                    if (!alive && code == null) text = "Il server sul tuo PC si è chiuso.";
                    else if (code == null) text = $"Il server {address}:{port} non risponde.";
                    LastReasonCode = code;
                    LastReasonText = text;
                    Fail(text);
                    yield break;
                }
                Debug.Log($"{NetConstants.ClientLog} Connessione non riuscita ({code ?? "nessuna risposta"}): riprovo tra {wait:0.0} s.");
                yield return new WaitForSecondsRealtime(wait);
                wait = Mathf.Min(wait * 2f, 3f);
                // Aspetta che la rete sia ferma prima di riprovare.
                while (_nm.ShutdownInProgress || _nm.IsListening) yield return null;
            }
        }

        bool StartClient(string address, ushort port, string playerName, float deadline)
        {
            var identity = ClientIdentity.LoadOrCreate(ClientIdentity.DefaultDirectory);
            var payload = identity.ToPayload(playerName);
            _nm.NetworkConfig.ConnectionData = payload.ToBytes();

            // Tentativi brevi del transport: i nuovi tentativi li fa ConnectWithRetry.
            float left = Mathf.Max(1f, deadline - Time.realtimeSinceStartup);
            _transport.ConnectTimeoutMS = 1000;
            _transport.MaxConnectAttempts = Mathf.Clamp(Mathf.CeilToInt(left), 1, 10);
            _transport.SetConnectionData(true, address, port);

            _clientStoppedSignal = false;
            _rawReason = null;
            _userDisconnect = false;
            LastReasonCode = null;
            LastReasonText = null;
            Debug.Log($"{NetConstants.ClientLog} Mi collego a {address}:{port} come '{playerName}' (giocatore {identity.PlayerId}).");
            _clientRunning = _nm.StartClient();
            return _clientRunning;
        }

        // ---------------- Eventi di Netcode ----------------

        void OnClientDisconnect(ulong clientId)
        {
            if (_nm == null || _nm.IsServer) return;
            // Il motivo del server va copiato subito.
            string reason = _nm.DisconnectReason;
            if (!string.IsNullOrEmpty(reason)) _rawReason = reason;
        }

        void OnClientStopped(bool wasHost)
        {
            if (_nm != null && string.IsNullOrEmpty(_rawReason))
            {
                string reason = _nm.DisconnectReason;
                if (!string.IsNullOrEmpty(reason)) _rawReason = reason;
            }
            bool wasInGame = State == LauncherState.InGame;
            _clientRunning = false;
            _clientStoppedSignal = true;
            ParseReason(_rawReason, out string code, out string text);
            Debug.Log($"{NetConstants.ClientLog} Client fermo. Motivo: {(code != null ? code + " - " + text : "nessuno")}.");

            if (_userDisconnect) return;
            if (wasInGame)
            {
                // Disconnessione inattesa durante la partita: mostra il motivo e torna al menu.
                LastReasonCode = code;
                LastReasonText = text ?? "Connessione con il server persa.";
                Fail("Disconnesso: " + LastReasonText);
                GameRoot.LoadMainMenuIfPresent();
            }
        }

        void OnLocalPlayerSpawned(PlayerNetwork player)
        {
            if (_nm == null || _nm.IsServer) return;
            SetState(LauncherState.InGame, _inGameStatus ?? "In gioco.");
            HudBus.SetArea(AreaInfo.DisplayName(GuessArea()));
        }

        /// <summary>Area della scena attiva (Area_&lt;Id&gt;), per l'HUD.</summary>
        static AreaId GuessArea()
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene.StartsWith("Area_", StringComparison.Ordinal) &&
                Enum.TryParse(scene.Substring(5), true, out AreaId a)) return a;
            return AreaId.Downtown;
        }

        /// <summary>Divide il motivo ricevuto in codice e testo (testo pronto da mostrare).</summary>
        static void ParseReason(string raw, out string code, out string text)
        {
            code = null;
            text = null;
            if (string.IsNullOrEmpty(raw)) return;
            // Netcode può mettere davanti il suo testo: cerco il nostro formato riga per riga.
            foreach (var line in raw.Split('\n'))
            {
                if (DisconnectReasons.TryParse(line.Trim(), out code, out text)) return;
            }
            code = null;
            text = null;
        }

        // ---------------- Server figlio ----------------

        bool TryStartChildServer(AreaId area, ushort port, string dataDir, bool forFriends, out string error)
        {
            error = null;
            string exe = ResolveServerExe(out error);
            if (exe == null) return false;
            try
            {
                Directory.CreateDirectory(dataDir);
                string ready = Path.Combine(dataDir, NetConstants.ReadyFileName);
                if (File.Exists(ready)) File.Delete(ready);
                string stop = Path.Combine(dataDir, NetConstants.StopFileName);
                if (File.Exists(stop)) File.Delete(stop);
            }
            catch (Exception e)
            {
                error = $"Cartella del server non valida: {dataDir} ({e.Message}).";
                return false;
            }

            int myPid;
            using (var me = Process.GetCurrentProcess()) myPid = me.Id;
            string listen = forFriends ? "0.0.0.0" : "127.0.0.1";
            string log = ChildLogPath(dataDir);
            string arguments = $"-server -area {area} -port {port} -listen {listen} -parentPid {myPid} " +
                               $"-datadir {Quote(dataDir)} -batchmode -nographics -logFile {Quote(log)}";
            try
            {
                var psi = new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                };
                _child?.Dispose();
                _child = Process.Start(psi);
                if (_child == null)
                {
                    error = "Il server sul tuo PC non è partito.";
                    return false;
                }
                _childForFriends = forFriends;
                _childPort = port;
                ChildServerDataDir = dataDir;
                Debug.Log($"{NetConstants.ClientLog} Server figlio avviato (pid {_child.Id}): {exe} {arguments}");
                return true;
            }
            catch (Exception e)
            {
                error = $"Non riesco ad avviare il server: {e.Message}";
                return false;
            }
        }

        static string ChildLogPath(string dataDir) => Path.Combine(dataDir, "server.log");

        static string Quote(string s) => s.IndexOf(' ') >= 0 ? "\"" + s + "\"" : s;

        /// <summary>
        /// Exe del server: nella build è questo stesso exe; nell'editor la build in Builds/Windows (se c'è).
        /// </summary>
        static string ResolveServerExe(out string error)
        {
            error = null;
#if UNITY_EDITOR
            string project = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
            string exe = Path.Combine(project, "Builds", "Windows", "UNDERWORLD.exe");
            if (File.Exists(exe)) return exe;
            error = "Nell'editor \"Gioca\" usa la build del gioco, ma non la trovo. Crea prima la build (UNDERWORLD > Build Windows).";
            return null;
#else
            try
            {
                using (var me = Process.GetCurrentProcess())
                {
                    string path = me.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                }
            }
            catch (Exception)
            {
                // Ripiego sotto.
            }
            string dataParent = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
            string guess = Path.Combine(dataParent, "UNDERWORLD.exe");
            if (File.Exists(guess)) return guess;
            error = "Non trovo l'eseguibile del gioco per avviare il server.";
            return null;
#endif
        }

        static bool IsReadyFileFor(string path, Process child)
        {
            if (child == null || !File.Exists(path)) return false;
            try
            {
                string text = File.ReadAllText(path);
                int childPid;
                try { childPid = child.Id; }
                catch (Exception) { return false; }
                return text.Contains("\"pid\":" + childPid + ",");
            }
            catch (Exception)
            {
                return false; // il server lo sta ancora scrivendo
            }
        }

        /// <summary>Chiude il server figlio (file di chiusura, poi Kill dopo 5 s). Serve quando cambia modo o porta.</summary>
        IEnumerator StopChildServer()
        {
            if (ChildServerPid == 0) yield break;
            Debug.Log($"{NetConstants.ClientLog} Chiudo il server figlio (pid {_child.Id}).");
            try
            {
                if (!string.IsNullOrEmpty(ChildServerDataDir))
                    File.WriteAllText(Path.Combine(ChildServerDataDir, NetConstants.StopFileName), "stop");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{NetConstants.ClientLog} File di chiusura non scritto: {e.Message}");
            }
            float until = Time.realtimeSinceStartup + 5f;
            while (ChildServerPid != 0 && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.2f);
            if (ChildServerPid != 0)
            {
                try { _child.Kill(); }
                catch (Exception) { /* già chiuso */ }
            }
        }

        // ---------------- Stato ----------------

        void StopFlow()
        {
            if (_flow != null) StopCoroutine(_flow);
            _flow = null;
        }

        /// <summary>Mostra un errore nato fuori dal launcher (es. argomento -connect sbagliato).</summary>
        internal void ReportFailure(string text) => Fail(text);

        void Fail(string text)
        {
            Debug.LogWarning($"{NetConstants.ClientLog} {text}");
            SetState(LauncherState.Failed, text);
        }

        void SetState(LauncherState state, string text)
        {
            text ??= string.Empty;
            if (state == State && text == StatusText) return;
            State = state;
            StatusText = text;
            Debug.Log($"{NetConstants.ClientLog} Stato: {state} - {text}");
            try { StateChanged?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
