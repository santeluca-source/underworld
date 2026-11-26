using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Underworld.Core;
using Underworld.Net;
using Underworld.Player;
using UnityEngine;

namespace Underworld.Testing
{
    /// <summary>Esito di un controllo.</summary>
    public sealed class AutoTestCheck
    {
        public string Name;
        public bool Ok;
        public string Detail;
        /// <summary>Secondi dall'inizio dello scenario.</summary>
        public float Time;
    }

    /// <summary>
    /// Strumenti per gli scenari: controlli, attese, log, accesso al giocatore locale e al bot.
    /// Le righe di log iniziano con [AutoTest].
    /// </summary>
    public sealed class AutoTestContext
    {
        public const string LogPrefix = "[AutoTest]";

        readonly List<AutoTestCheck> _checks = new List<AutoTestCheck>();
        readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        readonly float _startTime;

        internal AutoTestContext(string scenarioName, LaunchArgs args)
        {
            ScenarioName = scenarioName;
            Args = args;
            _startTime = UnityEngine.Time.realtimeSinceStartup;
        }

        public string ScenarioName { get; }
        public LaunchArgs Args { get; }
        /// <summary>Vero nei processi server (-server).</summary>
        public bool IsServer => Args.IsServer;
        /// <summary>Secondi dall'inizio dello scenario.</summary>
        public float Elapsed => UnityEngine.Time.realtimeSinceStartup - _startTime;

        public IReadOnlyList<AutoTestCheck> Checks => _checks;
        /// <summary>Dati liberi scritti nel risultato JSON (es. pid del server figlio).</summary>
        public IReadOnlyDictionary<string, object> Data => _data;

        /// <summary>Motivo dell'interruzione (Fail), o null.</summary>
        public string AbortReason { get; private set; }
        public bool Aborted => AbortReason != null;

        /// <summary>Esito dell'ultima WaitUntil.</summary>
        public bool LastWaitOk { get; private set; }

        // ---- Accesso al gioco ----

        /// <summary>Personaggio del giocatore locale (null sul server o prima dello spawn).</summary>
        public PlayerNetwork LocalPlayer => PlayerNetwork.Local;
        /// <summary>Comandi automatici del giocatore locale (null prima dello spawn).</summary>
        public BotInput Bot => PlayerNetwork.Local != null ? PlayerNetwork.Local.Bot : null;
        /// <summary>Launcher del client.</summary>
        public GameLauncher Launcher => GameRoot.Instance != null ? GameRoot.Instance.Launcher : null;
        /// <summary>Server di questo processo (null sui client).</summary>
        public ServerRuntime Server => ServerRuntime.Instance;

        // ---- Controlli e log ----

        /// <summary>Registra un controllo. Restituisce ok (comodo negli if).</summary>
        public bool Check(string name, bool ok, string detail = null)
        {
            _checks.Add(new AutoTestCheck { Name = name, Ok = ok, Detail = detail, Time = Elapsed });
            Debug.Log($"{LogPrefix} {(ok ? "OK  " : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
            return ok;
        }

        public void Log(string message) => Debug.Log($"{LogPrefix} {message}");

        /// <summary>Ferma lo scenario: risultato negativo.</summary>
        public void Fail(string reason)
        {
            AbortReason ??= string.IsNullOrEmpty(reason) ? "interrotto" : reason;
            Debug.LogWarning($"{LogPrefix} Scenario interrotto: {AbortReason}");
        }

        public void SetData(string key, object value) => _data[key] = value;

        /// <summary>Argomento -chiave del processo (o fallback).</summary>
        public string Arg(string key, string fallback = null) => Args.Get(key, fallback);

        // ---- Attese (da usare con yield return) ----

        /// <summary>Aspetta finché la condizione è vera o finisce il tempo. Esito in LastWaitOk.</summary>
        public IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what = null)
        {
            float until = UnityEngine.Time.realtimeSinceStartup + timeoutSeconds;
            while (true)
            {
                bool ok;
                try { ok = condition(); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    ok = false;
                }
                if (ok)
                {
                    LastWaitOk = true;
                    yield break;
                }
                if (UnityEngine.Time.realtimeSinceStartup > until)
                {
                    LastWaitOk = false;
                    if (!string.IsNullOrEmpty(what)) Log($"Tempo scaduto ({timeoutSeconds:0} s): {what}");
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>Aspetta alcuni secondi (tempo reale).</summary>
        public IEnumerator Wait(float seconds)
        {
            float until = UnityEngine.Time.realtimeSinceStartup + seconds;
            while (UnityEngine.Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>Aspetta il personaggio locale e registra il controllo "personaggio locale pronto".</summary>
        public IEnumerator WaitForLocalPlayer(float timeoutSeconds = 45f)
        {
            yield return WaitUntil(() => PlayerNetwork.Local != null && PlayerNetwork.Local.IsSpawned, timeoutSeconds, "personaggio locale");
            var launcher = Launcher;
            Check("personaggio locale pronto", LastWaitOk,
                LastWaitOk ? $"in {PlayerNetwork.Local.transform.position}" : $"stato del launcher: {launcher?.State} - {launcher?.StatusText}");
        }

        /// <summary>
        /// Aspetta il permesso di finire (file -autotest-release, creato da Tools/test.ps1 quando gli altri client
        /// hanno finito). Senza l'argomento non aspetta.
        /// </summary>
        public IEnumerator WaitForRelease(float timeoutSeconds)
        {
            string file = Args.Get("autotest-release");
            if (string.IsNullOrEmpty(file))
            {
                Log("Nessun file di rilascio (-autotest-release): non aspetto.");
                yield break;
            }
            yield return WaitUntil(() => File.Exists(file), timeoutSeconds, "rilascio da Tools/test.ps1");
        }

        /// <summary>Elenco dei nomi dei personaggi visti da questo processo (per i log).</summary>
        public static string VisiblePlayerNames()
        {
            var names = new List<string>();
            foreach (var p in PlayerNetwork.All) if (p != null) names.Add(p.Name);
            names.Sort(StringComparer.Ordinal);
            return string.Join(", ", names);
        }
    }
}
