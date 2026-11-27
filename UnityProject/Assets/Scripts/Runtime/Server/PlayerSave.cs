using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Underworld.Server
{
    /// <summary>Posizione salvata (x, y, z in metri).</summary>
    public struct SavedVector
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("z")] public float Z;

        public SavedVector(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    /// <summary>
    /// Salvataggio di un giocatore (contratto, sezione 7). File players/&lt;playerId&gt;.json, schema v1.
    /// Lo legge e lo scrive solo il server (IPlayerStore). Logica pura: nessuna chiamata a Unity.
    /// I moduli salvano i loro dati in Modules con GetModule/SetModule (JSON libero).
    /// </summary>
    public sealed class PlayerSave
    {
        /// <summary>Versione attuale dello schema. Un cambio di schema aggiunge un passo in PlayerSaveMigrations.</summary>
        public const int CurrentSchemaVersion = 1;

        [JsonProperty("schemaVersion")] public int SchemaVersion = CurrentSchemaVersion;
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("createdUtc")] public DateTime CreatedUtc;
        [JsonProperty("lastSeenUtc")] public DateTime LastSeenUtc;
        [JsonProperty("name")] public string Name;
        /// <summary>SHA256(playerId + ":" + token) in esadecimale minuscolo (IdentityHash). Mai il token.</summary>
        [JsonProperty("tokenHash")] public string TokenHash;
        /// <summary>Dollari interi. Lo cambia solo EconomyService.</summary>
        [JsonProperty("cash")] public long Cash;
        /// <summary>Id dell'oggetto → quantità (sempre &gt; 0). Lo cambia solo InventoryService.</summary>
        [JsonProperty("inventory")] public Dictionary<string, int> Inventory = new Dictionary<string, int>();
        /// <summary>Ultima area (AreaId come testo) e ultima posizione conosciuta dal server.</summary>
        [JsonProperty("lastArea")] public string LastArea;
        [JsonProperty("lastPosition")] public SavedVector LastPosition;
        /// <summary>Dati dei moduli: chiave del modulo → JSON libero.</summary>
        [JsonProperty("modules")] public Dictionary<string, JToken> Modules = new Dictionary<string, JToken>();

        /// <summary>Impostazioni JSON comuni (date in UTC, niente conversioni automatiche delle stringhe).</summary>
        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.None,
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.Indented,
        };

        static JsonSerializer _serializer;
        internal static JsonSerializer Serializer => _serializer ??= JsonSerializer.Create(JsonSettings);

        /// <summary>Nuovo profilo (soldi iniziali a parte: li dà EconomyService, così finiscono nel registro).</summary>
        public static PlayerSave CreateNew(string playerId, string name, string tokenHash, DateTime nowUtc)
        {
            return new PlayerSave
            {
                PlayerId = playerId,
                Name = name,
                TokenHash = tokenHash,
                CreatedUtc = nowUtc,
                LastSeenUtc = nowUtc,
                Cash = 0,
            };
        }

        // ---------------- Dati dei moduli ----------------

        /// <summary>Chiave valida: 1–64 caratteri a-z, 0-9, '.', '_', '-'.</summary>
        public static bool IsValidModuleKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 64) return false;
            foreach (char c in key)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        public bool HasModule(string key) => key != null && Modules != null && Modules.ContainsKey(key);

        /// <summary>Dati del modulo come T, o default(T) se mancano. Lancia un'eccezione se il JSON non è un T.</summary>
        public T GetModule<T>(string key)
        {
            if (!IsValidModuleKey(key)) throw new ArgumentException($"Chiave di modulo non valida: '{key}'", nameof(key));
            if (Modules == null || !Modules.TryGetValue(key, out var token) || token == null || token.Type == JTokenType.Null)
                return default;
            return token.ToObject<T>(Serializer);
        }

        /// <summary>Come GetModule, ma senza eccezioni: falso se mancano o non sono un T.</summary>
        public bool TryGetModule<T>(string key, out T value)
        {
            value = default;
            if (!HasModule(key)) return false;
            try
            {
                value = GetModule<T>(key);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Salva i dati del modulo (copia profonda in JSON). Ricordati di segnare la sessione come cambiata.</summary>
        public void SetModule<T>(string key, T value)
        {
            if (!IsValidModuleKey(key)) throw new ArgumentException($"Chiave di modulo non valida: '{key}'", nameof(key));
            Modules ??= new Dictionary<string, JToken>();
            Modules[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value, Serializer);
        }

        public bool RemoveModule(string key) => key != null && Modules != null && Modules.Remove(key);

        // ---------------- JSON ----------------

        public string ToJson() => JsonConvert.SerializeObject(this, JsonSettings);

        /// <summary>
        /// Controlla un salvataggio letto dal disco. Null se va bene, altrimenti il motivo (in italiano, per il log).
        /// </summary>
        public string Validate(string expectedPlayerId, long maxCash)
        {
            if (SchemaVersion != CurrentSchemaVersion) return $"schemaVersion {SchemaVersion} invece di {CurrentSchemaVersion}";
            if (!string.Equals(PlayerId, expectedPlayerId, StringComparison.Ordinal)) return $"playerId diverso ({PlayerId})";
            if (!IdentityHash.IsValidHash(TokenHash)) return "tokenHash mancante o non valido";
            if (Cash < 0 || Cash > maxCash) return $"cash fuori dai limiti ({Cash})";
            if (Inventory == null) Inventory = new Dictionary<string, int>();
            foreach (var kv in Inventory)
                if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) return $"inventario non valido ({kv.Key}={kv.Value})";
            if (Modules == null) Modules = new Dictionary<string, JToken>();
            return null;
        }
    }
}
