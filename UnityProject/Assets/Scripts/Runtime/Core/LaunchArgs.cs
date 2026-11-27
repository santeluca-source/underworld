using System;
using System.Collections.Generic;
using System.Globalization;

namespace Underworld.Core
{
    /// <summary>
    /// Argomenti della riga di comando. Un solo punto di lettura per tutto il gioco.
    /// Formato: "-chiave valore" oppure "-flag". Le chiavi non distinguono maiuscole.
    /// Elenco completo e significato: Docs/ARCHITECTURE.md, sezione "Modi di avvio".
    /// </summary>
    public sealed class LaunchArgs
    {
        static LaunchArgs _current;

        /// <summary>Argomenti del processo attuale (letti una volta).</summary>
        public static LaunchArgs Current => _current ??= Parse(Environment.GetCommandLineArgs());

        /// <summary>Solo per i test: sostituisce gli argomenti del processo.</summary>
        public static void OverrideForTests(LaunchArgs args) => _current = args;

        readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static LaunchArgs Parse(string[] args)
        {
            var result = new LaunchArgs();
            if (args == null) return result;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrEmpty(a) || a[0] != '-' || a.Length < 2) continue;
                string key = a.TrimStart('-');
                bool hasValue = i + 1 < args.Length && !IsKey(args[i + 1]);
                if (hasValue) { result._values[key] = args[i + 1]; i++; }
                else result._flags.Add(key);
            }
            return result;
        }

        // Un valore negativo ("-5") non è una chiave.
        static bool IsKey(string s) =>
            !string.IsNullOrEmpty(s) && s[0] == '-' && s.Length > 1 && !char.IsDigit(s[1]);

        public bool Has(string key) => _flags.Contains(key) || _values.ContainsKey(key);

        public string Get(string key, string fallback = null) =>
            _values.TryGetValue(key, out var v) ? v : fallback;

        public int GetInt(string key, int fallback) =>
            _values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : fallback;

        // ---- Valori usati dal gioco (nomi fissi, vedi ARCHITECTURE.md) ----

        /// <summary>Processo server senza grafica (-server).</summary>
        public bool IsServer => Has("server");

        /// <summary>Area del server (-area Downtown). Default Downtown.</summary>
        public AreaId Area => Enum.TryParse(Get("area", "Downtown"), true, out AreaId a) ? a : AreaId.Downtown;

        /// <summary>Porta del server (-port). Default: porta standard dell'area.</summary>
        public ushort Port => (ushort)GetInt("port", AreaInfo.DefaultPort(Area));

        /// <summary>Indirizzo su cui il server ascolta (-listen). Default 127.0.0.1 (solo questo PC).</summary>
        public string ListenAddress => Get("listen", "127.0.0.1");

        /// <summary>Cartella dati del server (-datadir). Null = cartella standard.</summary>
        public string DataDir => Get("datadir");

        /// <summary>-rundir: cartella di server.ready/server.stop (default: DataDir). Per più server con la stessa DataDir.</summary>
        public string RunDir => Get("rundir");

        /// <summary>File con l'elenco delle aree e dei loro indirizzi (-areas).</summary>
        public string AreasFile => Get("areas");

        /// <summary>Client: collegati subito a "ip:porta" (-connect).</summary>
        public string ConnectAddress => Get("connect");

        /// <summary>Client: nome del giocatore (-name).</summary>
        public string PlayerName => Get("name");

        /// <summary>Client: cartella dell'identità (-profile). Serve per più client sullo stesso PC.</summary>
        public string ProfileDir => Get("profile");

        /// <summary>Client: comportamento automatico (-bot walk|idle|...).</summary>
        public string Bot => Get("bot");

        /// <summary>Nome dello scenario di test automatico (-autotest).</summary>
        public string AutoTest => Get("autotest");

        /// <summary>File JSON dove scrivere il risultato del test (-autotest-out).</summary>
        public string AutoTestOut => Get("autotest-out");

        /// <summary>Screenshot automatico (-autoshot file.png).</summary>
        public string AutoShot => Get("autoshot");

        /// <summary>PID del processo padre: se muore, il server si chiude (-parentPid).</summary>
        public int ParentPid => GetInt("parentPid", 0);

        /// <summary>Server: abilita i comandi di test (-testmode). Mai nelle partite vere.</summary>
        public bool TestMode => Has("testmode");
    }
}
