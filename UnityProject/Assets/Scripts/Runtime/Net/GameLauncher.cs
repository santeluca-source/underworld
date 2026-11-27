using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Underworld.Core;
using Underworld.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Underworld.Net
{
    /// <summary>
    /// Avvio e connessione del client (IGameLauncher, registrato in GameServices.Launcher da GameRoot).
    /// - PlayLocal: avvia un server figlio (stesso exe, -server) solo per questo PC e si collega.
    /// - HostForFriends: come PlayLocal, ma il server ascolta su tutta la rete di casa (0.0.0.0).
    /// - Join: si collega a un server esistente.
    /// - Disconnect: esce dalla partita e torna al menu. QuitGame: chiude il gioco.
    /// I server figli si chiudono quando il launcher sparisce (uscita dal gioco, fine del Play nell'editor:
    /// file server.stop, poi Kill) e, come rete di sicurezza, quando questo processo esce (-parentPid).
    /// Argomenti per i test: -localport, -localdatadir, -localarea.
    /// </summary>
    public sealed class GameLauncher : MonoBehaviour, IGameLauncher
    {
        /// <summary>Nome usato quando il giocatore non ne ha scelto uno.</summary>
        public const string DefaultPlayerName = "Player";

        /// <summary>Tempo massimo per avviare il server locale e collegarsi (s).</summary>
        public const float LocalConnectTimeout = 25f;
        /// <summary>Tempo massimo per collegarsi a un server esistente (s).</summary>
        public const float JoinTimeout = 15f;
        /// <summary>Tempo massimo tra connessione e personaggio pronto (s).</summary>
        public const float SpawnTimeout = 30f;
        /// <summary>
        /// Attesa massima (s) quando il server ha ancora la sessione vecchia di questo giocatore (crash e riavvio):
        /// il server la chiude dopo il timeout del transport (30 s).
        /// </summary>
        public const float AlreadyConnectedGrace = 35f;
        /// <summary>Lunghezza massima del testo di un motivo mandato dal server.</summary>
        public const int MaxReasonLength = 200;

        public LauncherState State { get; private set; } = LauncherState.Idle;
        public string StatusText { get; private set; } = string.Empty;
        public event Action StateChanged;

        /// <summary>Codice dell'ultimo motivo di rifiuto o disconnessione (DisconnectReasons), o null.</summary>
        public string LastReasonCode { get; private set; }
        /// <summary>Testo (in inglese, ripulito) dell'ultimo motivo di rifiuto o disconnessione, o null.</summary>
        public string LastReasonText { get; private set; }

        /// <summary>Vero se l'ultimo errore è una disconnessione durante la partita (il menu mostra "Disconnected").</summary>
        public bool DisconnectedInGame { get; private set; }

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
        /// <summary>"ip:porta" per gli amici quando questo PC ospita (null altrimenti).</summary>
        string _hostAddress;

        internal void Init(NetworkManager nm, UnderworldTransport transport)
        {
            _nm = nm;
            _transport = transport;
            _nm.OnClientDisconnectCallback += OnClientDisconnect;
            _nm.OnClientStopped += OnClientStopped;
            PlayerNetwork.LocalPlayerSpawned += OnLocalPlayerSpawned;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        void OnDestroy()
        {
            if (_nm != null)
            {
                _nm.OnClientDisconnectCallback -= OnClientDisconnect;
                _nm.OnClientStopped -= OnClientStopped;
            }
            PlayerNetwork.LocalPlayerSpawned -= OnLocalPlayerSpawned;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            StopChildServerNow("launcher distrutto");
            _child?.Dispose();
            _child = null;
        }

        // Nell'editor il processo (Unity Editor) resta vivo dopo il Play: -parentPid non basta, il figlio va chiuso qui.
        void OnApplicationQuit() => StopChildServerNow("chiusura del gioco");

#if UNITY_EDITOR
        void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode) StopChildServerNow("fine del Play nell'editor");
        }
#endif

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
            HudBus.SetHostAddress(null);
            SetState(LauncherState.Idle, "You left the game.");
            // Se Netcode stava caricando l'area (Cancel durante la connessione), la scena attiva è ancora il menu:
            // OnActiveSceneChanged riporta al menu quando l'area si attiva.
            GameRoot.LoadMainMenuIfPresent();
        }

        public void QuitGame()
        {
            ShutdownForExit();
            Debug.Log($"{NetConstants.ClientLog} Chiusura del gioco.");
            // OnApplicationQuit chiude il server figlio (e -parentPid lo chiude comunque).
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
            _inGameStatus = $"Playing on {address}:{port}.";
            _hostAddress = null;
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
                Fail($"Unknown area: {areaArg}.");
                yield break;
            }
            ushort port = (ushort)Mathf.Clamp(args.GetInt("localport", AreaInfo.DefaultPort(area)), 1, 65535);
            string dataDir = args.Get("localdatadir");
            if (string.IsNullOrEmpty(dataDir)) dataDir = Path.Combine(Application.persistentDataPath, "LocalServer", area.ToString());
            dataDir = TrimDirectory(dataDir);
            float deadline = Time.realtimeSinceStartup + LocalConnectTimeout;

            // Un server figlio già acceso con le stesse impostazioni si riusa (es. dopo "Esci dalla partita").
            bool reuse = ChildServerPid != 0 && _childForFriends == forFriends && _childPort == port;
            if (!reuse)
            {
                yield return StopChildServer();
                // Un server rimasto acceso da una sessione di prima (es. Play dell'editor interrotto) sulla stessa porta.
                yield return StopOrphanServer(dataDir, port);
                SetState(LauncherState.StartingServer, "Starting the server on your PC...");
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
                        Fail($"The server on your PC closed right away. See the log: {ChildLogPath(dataDir)}");
                        yield break;
                    }
                    yield return new WaitForSecondsRealtime(0.25f);
                }
            }

            string lan = forFriends ? LocalNetworkInfo.GetLanIPv4() : null;
            _hostAddress = forFriends && lan != null ? $"{lan}:{port}" : null;
            _inGameStatus = forFriends
                ? (lan != null
                    ? $"Server open to friends: {lan}:{port}. Open UDP port {port} on your router for friends outside your home."
                    : $"Server open to friends on port {port}, but this PC's network address was not found.")
                : $"Playing on your PC: {AreaInfo.DisplayName(area)}.";

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
            bool graceGiven = false;
            while (true)
            {
                attempt++;
                SetState(LauncherState.Connecting, attempt == 1
                    ? $"Connecting to {address}:{port}..."
                    : $"Connecting to {address}:{port}... (attempt {attempt})");

                if (!StartClient(address, port, playerName, deadline))
                {
                    Fail("Could not start the connection. Check the address and port.");
                    yield break;
                }

                // Aspetta: personaggio pronto (in gioco) oppure client fermo (rifiuto, errore, timeout).
                float spawnDeadline = Time.realtimeSinceStartup + SpawnTimeout;
                while (!_clientStoppedSignal && State != LauncherState.InGame)
                {
                    if (Time.realtimeSinceStartup > spawnDeadline)
                    {
                        _nm.Shutdown();
                        Fail("The server did not create your character in time.");
                        yield break;
                    }
                    yield return null;
                }
                if (State == LauncherState.InGame) yield break;

                // Il client si è fermato: decidi se riprovare.
                ParseReason(_rawReason, out string code, out string text);
                // already_connected: di solito è la sessione vecchia dopo un crash; il server la chiude dopo il timeout.
                if (code == DisconnectReasons.AlreadyConnectedCode && !graceGiven)
                {
                    graceGiven = true;
                    deadline = Mathf.Max(deadline, Time.realtimeSinceStartup + AlreadyConnectedGrace);
                }
                bool retryable = code == null || code == DisconnectReasons.ServerStartingCode ||
                                 code == DisconnectReasons.AlreadyConnectedCode;
                bool alive = serverAlive == null || serverAlive();
                if (!retryable || !alive || Time.realtimeSinceStartup + wait > deadline)
                {
                    if (!alive && code == null) text = "The server on your PC has closed.";
                    else if (code == null) text = $"The server {address}:{port} is not responding.";
                    LastReasonCode = code;
                    LastReasonText = text;
                    Fail(text);
                    yield break;
                }
                Debug.Log($"{NetConstants.ClientLog} Connessione non riuscita ({code ?? "nessuna risposta"}): riprovo tra {wait:0.0} s.");
                if (code == DisconnectReasons.AlreadyConnectedCode)
                    SetState(LauncherState.Connecting, "Your last session is still open on the server. Waiting for it to close...");
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
            DisconnectedInGame = false;
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
                LastReasonText = text ?? "Lost connection to the server.";
                HudBus.SetHostAddress(null);
                DisconnectedInGame = true;
                // Il titolo "Disconnected" lo mette il menu: qui solo il motivo.
                Fail(LastReasonText);
                GameRoot.LoadMainMenuIfPresent();
            }
        }

        void OnLocalPlayerSpawned(PlayerNetwork player)
        {
            if (_nm == null || _nm.IsServer) return;
            SetState(LauncherState.InGame, _inGameStatus ?? "In game.");
            HudBus.SetArea(AreaInfo.DisplayName(GuessArea()));
            // "Play with friends": l'indirizzo deve vedersi nel gioco (toast ora, menu di pausa sempre).
            HudBus.SetHostAddress(_hostAddress);
            if (_hostAddress != null) HudBus.Toast($"Friends can join at {_hostAddress}", ToastKind.Info);
        }

        /// <summary>
        /// Un'area diventata attiva senza partita (Cancel mentre Netcode la caricava, errore durante il caricamento):
        /// si torna al menu invece di restare in un mondo vuoto senza personaggio.
        /// </summary>
        void OnActiveSceneChanged(Scene from, Scene to)
        {
            bool isServer = (_nm != null && _nm.IsServer) || LaunchArgs.Current.IsServer;
            bool connected = _nm != null && _nm.IsConnectedClient;
            if (!ShouldLeaveArea(to.name, isServer, State, connected, _userDisconnect)) return;
            Debug.Log($"{NetConstants.ClientLog} Area {to.name} caricata senza partita ({State}): torno al menu.");
            GameRoot.LoadMainMenuIfPresent();
        }

        /// <summary>Regola di OnActiveSceneChanged (logica pura, testata in EditMode).</summary>
        public static bool ShouldLeaveArea(string scene, bool isServer, LauncherState state, bool connected, bool userLeft)
        {
            if (isServer || string.IsNullOrEmpty(scene) || !scene.StartsWith("Area_", StringComparison.Ordinal)) return false;
            if (state != LauncherState.Idle && state != LauncherState.Failed) return false;
            return userLeft || !connected;
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
                if (DisconnectReasons.TryParse(line.Trim(), out code, out text))
                {
                    text = CleanReasonText(text);
                    return;
                }
            }
            code = null;
            text = null;
        }

        /// <summary>
        /// Il testo del server non è fidato: niente tag rich text (via i caratteri minore e maggiore), niente
        /// caratteri di controllo, al massimo MaxReasonLength caratteri. null se resta vuoto.
        /// </summary>
        public static string CleanReasonText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var sb = new StringBuilder(Math.Min(text.Length, MaxReasonLength));
            foreach (char c in text)
            {
                if (sb.Length >= MaxReasonLength) break;
                if (c == '<' || c == '>') continue;
                sb.Append(char.IsControl(c) ? ' ' : c);
            }
            string clean = sb.ToString().Trim();
            if (sb.Length >= MaxReasonLength && clean.Length > 0) clean += "...";
            return clean.Length == 0 ? null : clean;
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
                error = $"Invalid server folder: {dataDir} ({e.Message}).";
                return false;
            }

            int myPid;
            using (var me = Process.GetCurrentProcess()) myPid = me.Id;
            string listen = forFriends ? "0.0.0.0" : "127.0.0.1";
            string log = ChildLogPath(dataDir);
            string arguments = $"-server -area {area} -port {port} -listen {listen} -parentPid {myPid} " +
                               $"-datadir {QuoteArg(dataDir)} -batchmode -nographics -logFile {QuoteArg(log)}";
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
                    error = "The server on your PC did not start.";
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
                error = $"Could not start the server: {e.Message}";
                return false;
            }
        }

        static string ChildLogPath(string dataDir) => Path.Combine(dataDir, "server.log");

        /// <summary>
        /// Un argomento per la riga di comando di Windows (regole di CommandLineToArgvW): sempre tra virgolette,
        /// le barre rovesciate prima di una virgoletta (o della virgoletta finale) raddoppiate, le virgolette interne
        /// precedute da una barra. Così una cartella che finisce con la barra non diventa una virgoletta letterale.
        /// </summary>
        public static string QuoteArg(string s)
        {
            s ??= string.Empty;
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            int slashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"')
                {
                    sb.Append('\\', slashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', slashes);
                    sb.Append(c);
                }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>Cartella senza separatori finali (C:\dati\ diventa C:\dati), tranne la radice ("C:\", "/").</summary>
        public static string TrimDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return dir;
            string t = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (t.Length == 0 || t.EndsWith(":", StringComparison.Ordinal)) return dir;
            return t;
        }

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
            error = "In the editor, \"Play\" uses the game build, but it was not found. Make the build first (UNDERWORLD > Build Windows).";
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
            error = "The game executable was not found, so the server cannot start.";
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

        /// <summary>
        /// Chiude subito il server figlio (senza coroutine: OnDestroy, uscita, fine del Play nell'editor).
        /// File server.stop, attesa breve, poi Kill.
        /// </summary>
        void StopChildServerNow(string why)
        {
            if (ChildServerPid == 0) return;
            Debug.Log($"{NetConstants.ClientLog} Chiudo il server figlio (pid {ChildServerPid}): {why}.");
            try
            {
                if (!string.IsNullOrEmpty(ChildServerDataDir))
                    File.WriteAllText(Path.Combine(ChildServerDataDir, NetConstants.StopFileName), "stop");
            }
            catch (Exception) { /* si passa al Kill */ }
            try
            {
                if (!_child.WaitForExit(3000)) _child.Kill();
            }
            catch (Exception) { /* già chiuso */ }
        }

        /// <summary>
        /// Server rimasto acceso da prima nella stessa cartella dati e sulla stessa porta (il suo server.ready
        /// dice pid e porta): lo chiude (server.stop, poi Kill) prima di avviarne uno nuovo.
        /// Tocca solo un processo con lo stesso nome dell'exe del server.
        /// </summary>
        IEnumerator StopOrphanServer(string dataDir, ushort port)
        {
            string readyFile = Path.Combine(dataDir, NetConstants.ReadyFileName);
            if (!TryReadReadyFile(readyFile, out int pid, out int readyPort) || readyPort != port) yield break;
            Process orphan = null;
            try
            {
                orphan = Process.GetProcessById(pid);
                string exe = ResolveServerExe(out _);
                string expected = exe != null ? Path.GetFileNameWithoutExtension(exe) : "UNDERWORLD";
                if (orphan.HasExited || !string.Equals(orphan.ProcessName, expected, StringComparison.OrdinalIgnoreCase))
                {
                    orphan.Dispose();
                    yield break;
                }
            }
            catch (Exception)
            {
                orphan?.Dispose();
                yield break; // nessun processo con quel pid
            }

            Debug.Log($"{NetConstants.ClientLog} Trovato un server di prima ancora acceso (pid {pid}, porta {port}): lo chiudo.");
            try { File.WriteAllText(Path.Combine(dataDir, NetConstants.StopFileName), "stop"); }
            catch (Exception) { /* si passa al Kill */ }
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until && !HasExitedSafe(orphan)) yield return new WaitForSecondsRealtime(0.2f);
            try
            {
                if (!orphan.HasExited)
                {
                    orphan.Kill();
                    orphan.WaitForExit(2000);
                }
            }
            catch (Exception) { /* già chiuso */ }
            orphan.Dispose();
        }

        static bool HasExitedSafe(Process p)
        {
            try { return p.HasExited; }
            catch (Exception) { return true; }
        }

        /// <summary>Legge pid e porta da server.ready (formato scritto da ServerRuntime).</summary>
        public static bool TryReadReadyFile(string path, out int pid, out int port)
        {
            pid = 0;
            port = 0;
            try
            {
                if (!File.Exists(path)) return false;
                return TryParseReady(File.ReadAllText(path), out pid, out port);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryParseReady(string json, out int pid, out int port)
        {
            pid = 0;
            port = 0;
            if (string.IsNullOrEmpty(json)) return false;
            var mp = Regex.Match(json, "\"pid\"\\s*:\\s*(\\d+)");
            var mo = Regex.Match(json, "\"port\"\\s*:\\s*(\\d+)");
            return mp.Success && mo.Success && int.TryParse(mp.Groups[1].Value, out pid) &&
                   int.TryParse(mo.Groups[1].Value, out port) && pid > 0;
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
