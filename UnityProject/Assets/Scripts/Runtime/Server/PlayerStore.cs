using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Underworld.Server
{
    public enum PlayerLoadStatus
    {
        /// <summary>Nessun salvataggio: profilo nuovo.</summary>
        NotFound,
        Ok,
        /// <summary>Il file era rovinato; letto dalla copia .bak (il file rovinato è in players/corrupt/).</summary>
        RecoveredFromBackup,
        /// <summary>File e copia rovinati (spostati in players/corrupt/), o un file in quarantena: ingresso rifiutato.</summary>
        Corrupt,
        /// <summary>Errore del disco o salvataggio di una versione più nuova: ingresso rifiutato, niente quarantena.</summary>
        Error,
    }

    public readonly struct PlayerLoadResult
    {
        public readonly PlayerLoadStatus Status;
        public readonly PlayerSave Save;
        /// <summary>Spiegazione per il log (in italiano).</summary>
        public readonly string Detail;

        public PlayerLoadResult(PlayerLoadStatus status, PlayerSave save, string detail)
        {
            Status = status;
            Save = save;
            Detail = detail;
        }

        public bool HasSave => Save != null && (Status == PlayerLoadStatus.Ok || Status == PlayerLoadStatus.RecoveredFromBackup);
    }

    /// <summary>
    /// Archivio dei salvataggi dei giocatori (contratto, sezione 7). Oggi file JSON; domani un backend
    /// (ASP.NET Core + PostgreSQL) con la stessa interfaccia. Solo server. Le chiamate sono sincrone.
    /// </summary>
    public interface IPlayerStore
    {
        PlayerLoadResult Load(string playerId);
        /// <summary>Scrive il salvataggio. Lancia un'eccezione se non riesce.</summary>
        void Save(PlayerSave save);
    }

    /// <summary>
    /// Passi di migrazione dello schema di PlayerSave. Ogni passo porta il JSON dalla versione N alla N+1.
    /// Esempio per la v2: PlayerSaveMigrations.Register(1, root => { root["nuovoCampo"] = 0; }).
    /// Il numero di versione lo aggiorna Migrate, non il passo.
    /// </summary>
    public static class PlayerSaveMigrations
    {
        static readonly Dictionary<int, Action<JObject>> Steps = new Dictionary<int, Action<JObject>>();

        public static void Register(int fromVersion, Action<JObject> step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (fromVersion < 1 || fromVersion >= PlayerSave.CurrentSchemaVersion)
                throw new ArgumentOutOfRangeException(nameof(fromVersion), "La migrazione deve partire da una versione vecchia.");
            Steps[fromVersion] = step;
        }

        public enum Outcome { Ok, TooNew, Invalid }

        /// <summary>Porta il JSON alla versione attuale. TooNew = salvataggio di un server più nuovo (non toccarlo).</summary>
        public static Outcome Migrate(JObject root, out string detail)
        {
            detail = null;
            var v = root["schemaVersion"];
            if (v == null || v.Type != JTokenType.Integer) { detail = "schemaVersion mancante"; return Outcome.Invalid; }
            int version = v.Value<int>();
            if (version < 1) { detail = $"schemaVersion {version} non valida"; return Outcome.Invalid; }
            if (version > PlayerSave.CurrentSchemaVersion)
            {
                detail = $"salvataggio della versione {version}, questo server conosce la {PlayerSave.CurrentSchemaVersion}";
                return Outcome.TooNew;
            }
            while (version < PlayerSave.CurrentSchemaVersion)
            {
                if (!Steps.TryGetValue(version, out var step)) { detail = $"manca la migrazione dalla versione {version}"; return Outcome.Invalid; }
                step(root);
                version++;
                root["schemaVersion"] = version;
            }
            return Outcome.Ok;
        }
    }

    /// <summary>
    /// Salvataggi in &lt;datadir&gt;/players/&lt;playerId&gt;.json.
    /// - Il playerId è controllato (PlayerIdPath) prima di ogni percorso.
    /// - Scrittura atomica: file .tmp, poi File.Replace che tiene la versione precedente in .json.bak.
    /// - Lettura: file rovinato → copia .bak (il file rovinato va in players/corrupt/). Entrambi rovinati →
    ///   tutti e due in players/corrupt/ e Corrupt: il server rifiuta l'ingresso e non crea mai un profilo vuoto
    ///   al loro posto. Finché c'è un file in quarantena per quel giocatore, l'ingresso resta bloccato
    ///   (il proprietario del server lo ripara o lo cancella a mano).
    /// </summary>
    public sealed class JsonFilePlayerStore : IPlayerStore
    {
        public const string PlayersFolder = "players";
        public const string CorruptFolder = "corrupt";

        public string PlayersDir { get; }
        public string CorruptDir { get; }
        readonly long _maxCash;

        public JsonFilePlayerStore(string dataDir, long maxCash)
        {
            if (string.IsNullOrEmpty(dataDir)) throw new ArgumentException("Cartella dati mancante", nameof(dataDir));
            PlayersDir = Path.Combine(dataDir, PlayersFolder);
            CorruptDir = Path.Combine(PlayersDir, CorruptFolder);
            _maxCash = maxCash;
            Directory.CreateDirectory(PlayersDir);
        }

        public string PathFor(string playerId) => PlayerIdPath.Combine(PlayersDir, playerId, ".json");
        public string BackupPathFor(string playerId) => PathFor(playerId) + ".bak";

        /// <summary>Vero se un file di questo giocatore è in players/corrupt/.</summary>
        public bool IsQuarantined(string playerId)
        {
            if (!PlayerIdPath.IsValid(playerId) || !Directory.Exists(CorruptDir)) return false;
            return Directory.GetFiles(CorruptDir, playerId + ".*").Length > 0;
        }

        public PlayerLoadResult Load(string playerId)
        {
            if (!PlayerIdPath.IsValid(playerId))
                return new PlayerLoadResult(PlayerLoadStatus.Error, null, "playerId non valido");

            string path = PathFor(playerId);
            string bak = BackupPathFor(playerId);
            bool mainExists = File.Exists(path);
            bool bakExists = File.Exists(bak);

            if (!mainExists && !bakExists)
            {
                if (IsQuarantined(playerId))
                    return new PlayerLoadResult(PlayerLoadStatus.Corrupt, null, "salvataggio in quarantena in players/corrupt");
                return new PlayerLoadResult(PlayerLoadStatus.NotFound, null, null);
            }

            string mainProblem = mainExists ? null : "file principale mancante";
            if (mainExists)
            {
                var r = TryRead(path, playerId, out var save, out mainProblem);
                if (r == ReadOutcome.Ok) return new PlayerLoadResult(PlayerLoadStatus.Ok, save, null);
                if (r == ReadOutcome.IoError || r == ReadOutcome.TooNew)
                    return new PlayerLoadResult(PlayerLoadStatus.Error, null, mainProblem);
            }

            if (bakExists)
            {
                var r = TryRead(bak, playerId, out var save, out string bakProblem);
                if (r == ReadOutcome.Ok)
                {
                    // Il file principale rovinato va in quarantena; la copia buona torna file principale.
                    if (mainExists) Quarantine(path);
                    WriteMain(save);
                    return new PlayerLoadResult(PlayerLoadStatus.RecoveredFromBackup, save, mainProblem);
                }
                if (r == ReadOutcome.IoError || r == ReadOutcome.TooNew)
                    return new PlayerLoadResult(PlayerLoadStatus.Error, null, bakProblem);
                mainProblem += "; copia .bak: " + bakProblem;
            }

            if (mainExists) Quarantine(path);
            if (bakExists) Quarantine(bak);
            return new PlayerLoadResult(PlayerLoadStatus.Corrupt, null, mainProblem);
        }

        public void Save(PlayerSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            WriteMain(save);
        }

        // ---------------- Interni ----------------

        enum ReadOutcome { Ok, Corrupt, TooNew, IoError }

        ReadOutcome TryRead(string file, string playerId, out PlayerSave save, out string problem)
        {
            save = null;
            problem = null;
            string text;
            try
            {
                text = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                problem = "lettura non riuscita: " + e.Message;
                return ReadOutcome.IoError;
            }

            try
            {
                JObject root;
                using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
                    root = JObject.Load(reader);
                var outcome = PlayerSaveMigrations.Migrate(root, out problem);
                if (outcome == PlayerSaveMigrations.Outcome.TooNew) return ReadOutcome.TooNew;
                if (outcome != PlayerSaveMigrations.Outcome.Ok) return ReadOutcome.Corrupt;
                save = root.ToObject<PlayerSave>(PlayerSave.Serializer);
                problem = save?.Validate(playerId, _maxCash) ?? "file vuoto";
                if (problem != null) { save = null; return ReadOutcome.Corrupt; }
                return ReadOutcome.Ok;
            }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidCastException || e is FormatException)
            {
                problem = "JSON non valido: " + e.Message;
                save = null;
                return ReadOutcome.Corrupt;
            }
        }

        void WriteMain(PlayerSave save)
        {
            string path = PathFor(save.PlayerId);
            string bak = path + ".bak";
            string tmp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(save.ToJson());
            Retry(() =>
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
                if (File.Exists(path)) File.Replace(tmp, path, bak, true);
                else File.Move(tmp, path);
            });
        }

        void Quarantine(string file)
        {
            try
            {
                Directory.CreateDirectory(CorruptDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                // Nome: <playerId>.<ora>.json oppure <playerId>.<ora>.json.bak
                string name = Path.GetFileName(file);
                int dot = name.IndexOf('.');
                string target = Path.Combine(CorruptDir, name.Substring(0, dot) + "." + stamp + name.Substring(dot));
                File.Move(file, target);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Se non si sposta resta dov'è: l'ingresso è rifiutato comunque.
                UnityEngine.Debug.LogWarning($"[Save] Quarantena non riuscita per {file}: {e.Message}");
            }
        }

        static void Retry(Action action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    // Antivirus o indicizzazione possono tenere il file per un attimo.
                    Thread.Sleep(15 * attempt);
                }
            }
        }
    }
}
