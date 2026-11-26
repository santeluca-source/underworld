using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Underworld.Core;
using Underworld.Net;
using Underworld.Player;
using Unity.Netcode;
using UnityEngine;

namespace Underworld.Testing
{
    /// <summary>
    /// Esegue lo scenario di test chiesto con -autotest &lt;nome&gt; (contratto, sezione 12).
    /// - Scrive il risultato JSON in -autotest-out (se c'è).
    /// - Client: alla fine chiude il gioco con codice 0 (passato) o 1 (fallito).
    /// - Server: alla fine scrive il risultato e resta acceso (lo chiude Tools/test.ps1 con il file
    ///   server.stop); il codice di uscita del server diventa 1 se lo scenario è fallito.
    /// - Tempo massimo: -autotest-timeout &lt;secondi&gt; (default 120).
    /// - Quando il personaggio locale è pronto scrive il file &lt;out&gt;.ingame (Tools/test.ps1 lo aspetta).
    /// Lo avvia GameRoot.
    /// </summary>
    public sealed class AutoTestRunner : MonoBehaviour
    {
        public const float DefaultTimeoutSeconds = 120f;

        /// <summary>Il runner di questo processo (null senza -autotest).</summary>
        public static AutoTestRunner Instance { get; private set; }

        /// <summary>Vero quando lo scenario è finito con un esito negativo.</summary>
        public static bool Failed { get; private set; }

        /// <summary>Vero quando lo scenario è finito (passato o no).</summary>
        public static bool Finished { get; private set; }

        AutoTestContext _ctx;
        string _outPath;
        int _exceptions;
        bool _resultWritten;

        /// <summary>Avvia il runner se il processo ha -autotest. Lo chiama GameRoot.</summary>
        public static void StartIfRequested(MonoBehaviour root)
        {
            var args = LaunchArgs.Current;
            if (string.IsNullOrEmpty(args.AutoTest) || Instance != null) return;
            var go = new GameObject("AutoTestRunner");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AutoTestRunner>();
        }

        void Awake()
        {
            Application.logMessageReceived += OnLog;
            PlayerNetwork.LocalPlayerSpawned += OnLocalPlayerSpawned;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            PlayerNetwork.LocalPlayerSpawned -= OnLocalPlayerSpawned;
            if (Instance == this) Instance = null;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception) _exceptions++;
        }

        void OnLocalPlayerSpawned(PlayerNetwork player)
        {
            if (string.IsNullOrEmpty(_outPath)) return;
            try { File.WriteAllText(_outPath + ".ingame", "ingame"); }
            catch (Exception e) { Debug.LogWarning($"{AutoTestContext.LogPrefix} File .ingame non scritto: {e.Message}"); }
        }

        IEnumerator Start()
        {
            var args = LaunchArgs.Current;
            string name = args.AutoTest;
            _outPath = args.AutoTestOut;
            float timeout = args.GetInt("autotest-timeout", (int)DefaultTimeoutSeconds);
            _ctx = new AutoTestContext(name, args);
            Debug.Log($"{AutoTestContext.LogPrefix} Scenario '{name}' ({(args.IsServer ? "server" : "client")}), tempo massimo {timeout:0} s.");

            if (!string.IsNullOrEmpty(_outPath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(_outPath));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{AutoTestContext.LogPrefix} Cartella del risultato non creata: {e.Message}");
                }
            }

            IAutoTestScenario scenario = CreateScenario(name, out string error);
            if (scenario == null)
            {
                _ctx.Fail(error);
                Finish();
                yield break;
            }

            // Il server esegue lo scenario solo quando l'area è pronta.
            if (args.IsServer)
            {
                yield return _ctx.WaitUntil(() => ServerRuntime.Instance != null && (ServerRuntime.Instance.IsReady || ServerRuntime.Instance.IsStopping), 60f, "server pronto");
                if (!_ctx.LastWaitOk || ServerRuntime.Instance == null || !ServerRuntime.Instance.IsReady)
                {
                    _ctx.Fail("il server non è diventato pronto");
                    Finish();
                    yield break;
                }
            }

            float deadline = Time.realtimeSinceStartup + timeout;
            IEnumerator body;
            try { body = scenario.Run(_ctx); }
            catch (Exception e)
            {
                Debug.LogException(e);
                _ctx.Fail("eccezione all'avvio dello scenario: " + e.Message);
                Finish();
                yield break;
            }

            // Esecuzione "a mano" delle coroutine annidate: così le eccezioni si possono prendere.
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            while (stack.Count > 0)
            {
                if (_ctx.Aborted) break;
                if (Time.realtimeSinceStartup > deadline)
                {
                    _ctx.Fail($"tempo scaduto ({timeout:0} s)");
                    break;
                }
                var top = stack.Peek();
                bool moved;
                try { moved = top.MoveNext(); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    _ctx.Fail("eccezione nello scenario: " + e.Message);
                    break;
                }
                if (!moved)
                {
                    stack.Pop();
                    continue;
                }
                object yielded = top.Current;
                if (yielded is IEnumerator nested) stack.Push(nested);
                else yield return yielded; // null, WaitForSeconds, ecc.
            }

            Finish();
        }

        /// <summary>Chiude subito lo scenario con un esito negativo (es. il server si chiude prima della fine).</summary>
        public void AbortAndFinish(string reason)
        {
            if (Finished) return;
            if (_ctx == null) _ctx = new AutoTestContext(LaunchArgs.Current.AutoTest, LaunchArgs.Current);
            _ctx.Fail(reason);
            Finish();
        }

        static IAutoTestScenario CreateScenario(string name, out string error)
        {
            error = null;
            Type found = null;
            foreach (var type in typeof(AutoTestRunner).Assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<AutoTestScenarioAttribute>();
                if (attr == null || !string.Equals(attr.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                found = type;
                break;
            }
            if (found == null)
            {
                error = $"scenario sconosciuto: '{name}'";
                return null;
            }
            if (!typeof(IAutoTestScenario).IsAssignableFrom(found) || found.GetConstructor(Type.EmptyTypes) == null)
            {
                error = $"la classe {found.Name} non è un IAutoTestScenario con costruttore vuoto";
                return null;
            }
            return (IAutoTestScenario)Activator.CreateInstance(found);
        }

        void Finish()
        {
            if (Finished) return;
            Finished = true;

            bool allOk = _ctx.Checks.Count > 0 && !_ctx.Aborted;
            foreach (var c in _ctx.Checks) if (!c.Ok) allOk = false;
            Failed = !allOk;

            WriteResult(allOk);
            Debug.Log($"{AutoTestContext.LogPrefix} RISULTATO {(allOk ? "PASSATO" : "FALLITO")}: {_ctx.ScenarioName} " +
                      $"(controlli {_ctx.Checks.Count}, eccezioni nel log {_exceptions}{(_ctx.Aborted ? ", " + _ctx.AbortReason : "")}).");

            if (_ctx.IsServer)
            {
                // Il server resta acceso per gli altri processi: lo chiude Tools/test.ps1.
                return;
            }
            StartCoroutine(QuitClient(allOk ? 0 : 1));
        }

        void WriteResult(bool passed)
        {
            if (_resultWritten || string.IsNullOrEmpty(_outPath)) return;
            _resultWritten = true;
            var checks = new List<object>();
            foreach (var c in _ctx.Checks) checks.Add(new { name = c.Name, ok = c.Ok, detail = c.Detail, time = Math.Round(c.Time, 2) });
            var result = new
            {
                scenario = _ctx.ScenarioName,
                role = _ctx.IsServer ? "server" : "client",
                name = _ctx.Args.PlayerName,
                passed,
                aborted = _ctx.AbortReason,
                exceptions = _exceptions,
                durationSec = Math.Round(_ctx.Elapsed, 2),
                checks,
                data = _ctx.Data,
            };
            try
            {
                string tmp = _outPath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(result, Formatting.Indented));
                if (File.Exists(_outPath)) File.Delete(_outPath);
                File.Move(tmp, _outPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"{AutoTestContext.LogPrefix} Risultato non scritto in {_outPath}: {e.Message}");
            }
        }

        IEnumerator QuitClient(int exitCode)
        {
            var nm = NetworkManager.Singleton;
            var launcher = GameRoot.Instance != null ? GameRoot.Instance.Launcher : null;
            if (launcher != null) launcher.ShutdownForExit();
            if (nm != null && nm.IsListening)
            {
                if (!nm.ShutdownInProgress) nm.Shutdown();
                float until = Time.realtimeSinceStartup + 2f;
                while (nm != null && (nm.IsListening || nm.ShutdownInProgress) && Time.realtimeSinceStartup < until) yield return null;
            }
            Debug.Log($"{AutoTestContext.LogPrefix} Chiudo il client (codice {exitCode}).");
            Application.Quit(exitCode);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Failed = false;
            Finished = false;
        }
    }
}
