using System;
using System.Text;
using Newtonsoft.Json;

namespace Underworld.Net
{
    /// <summary>
    /// Dati che il client manda nella richiesta di connessione (NetworkConfig.ConnectionData).
    /// JSON: { "v": 1, "playerId": "guid", "token": "base64", "name": "Nome", "ticket": null }.
    /// Il server controlla tutto (ConnectionApprovalRules): non si fida mai di questi dati.
    /// </summary>
    [Serializable]
    public sealed class ConnectionPayload
    {
        /// <summary>Versione del protocollo (NetConstants.ProtocolVersion).</summary>
        [JsonProperty("v")] public int Version;

        /// <summary>GUID del giocatore (ClientIdentity).</summary>
        [JsonProperty("playerId")] public string PlayerId;

        /// <summary>Segreto del giocatore: 32 byte casuali in base64. La Fase 5 ne controlla l'hash.</summary>
        [JsonProperty("token")] public string Token;

        /// <summary>Nome visibile scelto dal giocatore.</summary>
        [JsonProperty("name")] public string Name;

        /// <summary>Biglietto di trasferimento tra aree (Fase 11). Null per un ingresso normale.</summary>
        [JsonProperty("ticket")] public string Ticket;

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            MaxDepth = 8,
        };

        /// <summary>JSON pronto da mandare.</summary>
        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None, Settings);

        /// <summary>Byte UTF-8 per NetworkConfig.ConnectionData.</summary>
        public byte[] ToBytes() => Encoding.UTF8.GetBytes(ToJson());

        /// <summary>
        /// Legge il JSON. Restituisce null se i dati non sono un JSON valido o non sono un oggetto.
        /// Non lancia eccezioni: i dati arrivano dalla rete e possono essere qualsiasi cosa.
        /// </summary>
        public static ConnectionPayload TryParse(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                string json = Encoding.UTF8.GetString(data).Trim();
                if (json.Length < 2 || json[0] != '{') return null;
                return JsonConvert.DeserializeObject<ConnectionPayload>(json, Settings);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>True se il token è base64 valido di esattamente 32 byte.</summary>
        public static bool IsValidToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 64) return false;
            try
            {
                return Convert.FromBase64String(token).Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>Normalizza un GUID nel formato "d" minuscolo. Falso se non è un GUID.</summary>
        public static bool TryNormalizePlayerId(string raw, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(raw) || raw.Length > 64) return false;
            if (!Guid.TryParse(raw, out var g) || g == Guid.Empty) return false;
            normalized = g.ToString("D");
            return true;
        }
    }
}
