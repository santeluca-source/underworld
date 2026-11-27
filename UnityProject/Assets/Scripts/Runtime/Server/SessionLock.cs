using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Underworld.Server
{
    /// <summary>Contenuto di un file locks/&lt;playerId&gt;.lock.</summary>
    public sealed class SessionLockInfo
    {
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("area")] public string Area;
        [JsonProperty("pid")] public int Pid;
        [JsonProperty("port")] public int Port;
        /// <summary>Ora dell'ultimo battito (UTC, formato "o").</summary>
        [JsonProperty("heartbeatUtc")] public string HeartbeatUtc;

        public bool TryGetHeartbeat(out DateTime utc) =>
            DateTime.TryParse(HeartbeatUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
    }

    public enum LockDecision
    {
        /// <summary>Nessun lucchetto: si può prendere.</summary>
        Free,
        /// <summary>Lucchetto di questo stesso server (stesso pid e porta).</summary>
        Own,
        /// <summary>Lucchetto di un altro server senza battito da più di StaleSeconds: si può prendere.</summary>
        Stale,
        /// <summary>Lucchetto vivo di un altro server: rifiuto already_connected.</summary>
        HeldByOther,
    }

    /// <summary>Regole del lucchetto di sessione (logica pura, test EditMode).</summary>
    public static class SessionLockRules
    {
        /// <summary>Un lucchetto senza battito da più di questi secondi è scaduto.</summary>
        public const double StaleSeconds = 15.0;
        /// <summary>Ogni quanto il server rinnova i suoi lucchetti.</summary>
        public const float HeartbeatSeconds = 5f;

        /// <summary>
        /// Decide cosa fare di un lucchetto esistente. info null = file illeggibile: conta l'ora del file
        /// (fileWriteUtc), così un file rovinato non blocca il giocatore per sempre.
        /// </summary>
        public static LockDecision Decide(SessionLockInfo info, DateTime fileWriteUtc, int myPid, int myPort, DateTime nowUtc)
        {
            if (info != null && info.Pid == myPid && info.Port == myPort) return LockDecision.Own;
            DateTime beat = fileWriteUtc;
            if (info != null && info.TryGetHeartbeat(out DateTime parsed)) beat = parsed;
            double age = (nowUtc - beat).TotalSeconds;
            return age > StaleSeconds ? LockDecision.Stale : LockDecision.HeldByOther;
        }
    }

    /// <summary>
    /// Lucchetti di sessione in &lt;datadir&gt;/locks/&lt;playerId&gt;.lock (contratto, sezione 6).
    /// Un giocatore può essere in gioco su un solo server alla volta, anche se più server condividono DataDir.
    /// Presa con CreateNew (due server non possono vincere entrambi), battito con scrittura atomica.
    /// Solo server.
    /// </summary>
    public sealed class SessionLockStore
    {
        public const string LocksFolder = "locks";

        public string LocksDir { get; }
        public string Area { get; }
        public int Pid { get; }
        public int Port { get; }
        readonly Func<DateTime> _clock;

        public SessionLockStore(string dataDir, string area, int pid, int port, Func<DateTime> clock = null)
        {
            LocksDir = Path.Combine(dataDir, LocksFolder);
            Area = area;
            Pid = pid;
            Port = port;
            _clock = clock ?? (() => DateTime.UtcNow);
            Directory.CreateDirectory(LocksDir);
        }

        public string PathFor(string playerId) => PlayerIdPath.Combine(LocksDir, playerId, ".lock");

        /// <summary>Prova a prendere il lucchetto. Falso se un altro server lo tiene vivo (holder = il suo contenuto).</summary>
        public bool TryAcquire(string playerId, out SessionLockInfo holder)
        {
            holder = null;
            string path = PathFor(playerId);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (TryCreateNew(path, playerId)) return true;

                var info = Read(path, out DateTime writeUtc, out bool exists);
                if (!exists) continue; // tolto nel frattempo: riprova a crearlo
                var decision = SessionLockRules.Decide(info, writeUtc, Pid, Port, _clock());
                switch (decision)
                {
                    case LockDecision.Own:
                        WriteAtomic(path, playerId);
                        return true;
                    case LockDecision.Stale:
                        try { File.Delete(path); }
                        catch (IOException) { /* un altro server lo sta prendendo: riprova */ }
                        continue;
                    default:
                        holder = info;
                        return false;
                }
            }
            holder = Read(path, out _, out _);
            return false;
        }

        /// <summary>Rinnova il battito. Falso se il lucchetto non è più di questo server.</summary>
        public bool Heartbeat(string playerId)
        {
            string path = PathFor(playerId);
            var info = Read(path, out DateTime writeUtc, out bool exists);
            if (exists && info == null)
            {
                // File illeggibile (scrittura a metà di un altro processo?): non lo tocco, riprovo al prossimo battito.
                return true;
            }
            if (exists && (info.Pid != Pid || info.Port != Port)) return false;
            if (!exists) return TryCreateNew(path, playerId);
            WriteAtomic(path, playerId);
            return true;
        }

        /// <summary>Libera il lucchetto solo se è di questo server.</summary>
        public void Release(string playerId)
        {
            string path = PathFor(playerId);
            var info = Read(path, out _, out bool exists);
            if (!exists) return;
            if (info != null && (info.Pid != Pid || info.Port != Port)) return;
            try { File.Delete(path); }
            catch (IOException) { /* scade da solo dopo StaleSeconds */ }
        }

        public bool IsHeldByMe(string playerId)
        {
            var info = Read(PathFor(playerId), out _, out bool exists);
            return exists && info != null && info.Pid == Pid && info.Port == Port;
        }

        // ---------------- Interni ----------------

        byte[] Content(string playerId)
        {
            var info = new SessionLockInfo
            {
                PlayerId = playerId,
                Area = Area,
                Pid = Pid,
                Port = Port,
                HeartbeatUtc = _clock().ToString("o", CultureInfo.InvariantCulture),
            };
            return new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(info));
        }

        bool TryCreateNew(string path, string playerId)
        {
            try
            {
                byte[] bytes = Content(playerId);
                using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    fs.Write(bytes, 0, bytes.Length);
                return true;
            }
            catch (IOException)
            {
                return false; // esiste già
            }
        }

        void WriteAtomic(string path, string playerId)
        {
            string tmp = path + "." + Pid + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, Content(playerId));
                if (File.Exists(path)) File.Replace(tmp, path, null, true);
                else File.Move(tmp, path);
            }
            catch (IOException e)
            {
                UnityEngine.Debug.LogWarning($"[Save] Battito del lucchetto non scritto ({Path.GetFileName(path)}): {e.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            }
        }

        static SessionLockInfo Read(string path, out DateTime writeUtc, out bool exists)
        {
            writeUtc = DateTime.MinValue;
            exists = File.Exists(path);
            if (!exists) return null;
            try
            {
                writeUtc = File.GetLastWriteTimeUtc(path);
                string text = File.ReadAllText(path, Encoding.UTF8);
                return JsonConvert.DeserializeObject<SessionLockInfo>(text);
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
