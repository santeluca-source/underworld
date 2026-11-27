using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Underworld.Core;
using UnityEngine;

namespace Underworld.Net
{
    /// <summary>
    /// Identità provvisoria del client (contratto, sezione 5): playerId (GUID) + token (32 byte casuali, base64).
    /// File identity.json nella cartella -profile, oppure in persistentDataPath/Client.
    /// Il token è un segreto: non va mai scritto nei log.
    /// </summary>
    public sealed class ClientIdentity
    {
        public const string FileName = "identity.json";

        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("token")] public string Token;
        [JsonProperty("createdUtc")] public string CreatedUtc;

        /// <summary>Cartella standard dell'identità per questo processo.</summary>
        public static string DefaultDirectory
        {
            get
            {
                string dir = LaunchArgs.Current.ProfileDir;
                return string.IsNullOrEmpty(dir) ? Path.Combine(Application.persistentDataPath, "Client") : dir;
            }
        }

        /// <summary>Crea una identità nuova (GUID + 32 byte casuali).</summary>
        public static ClientIdentity CreateNew()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return new ClientIdentity
            {
                PlayerId = Guid.NewGuid().ToString("D"),
                Token = Convert.ToBase64String(bytes),
                CreatedUtc = DateTime.UtcNow.ToString("o"),
            };
        }

        /// <summary>True se GUID e token hanno il formato giusto.</summary>
        public bool IsValid() =>
            ConnectionPayload.TryNormalizePlayerId(PlayerId, out _) && ConnectionPayload.IsValidToken(Token);

        /// <summary>
        /// Legge identity.json dalla cartella; se manca o è rovinato ne crea uno nuovo e lo salva.
        /// Un file rovinato viene tenuto come identity.json.bad (per capire cosa è successo).
        /// </summary>
        public static ClientIdentity LoadOrCreate(string directory)
        {
            if (string.IsNullOrEmpty(directory)) directory = DefaultDirectory;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName);

            if (File.Exists(path))
            {
                try
                {
                    var loaded = JsonConvert.DeserializeObject<ClientIdentity>(File.ReadAllText(path));
                    if (loaded != null && loaded.IsValid())
                    {
                        ConnectionPayload.TryNormalizePlayerId(loaded.PlayerId, out loaded.PlayerId);
                        return loaded;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{NetConstants.ClientLog} identity.json non leggibile: {e.Message}");
                }
                try
                {
                    string bad = path + ".bad";
                    if (File.Exists(bad)) File.Delete(bad);
                    File.Move(path, bad);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{NetConstants.ClientLog} Non riesco a spostare l'identità rovinata: {e.Message}");
                }
                Debug.LogWarning($"{NetConstants.ClientLog} Identità rovinata: ne creo una nuova.");
            }

            var created = CreateNew();
            Save(created, path);
            Debug.Log($"{NetConstants.ClientLog} Nuova identità creata in {directory} (giocatore {created.PlayerId}).");
            return created;
        }

        /// <summary>Scrittura atomica: file temporaneo, poi sostituzione.</summary>
        static void Save(ClientIdentity identity, string path)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(identity, Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>Payload di connessione con questa identità.</summary>
        public ConnectionPayload ToPayload(string playerName, string ticket = null) => new ConnectionPayload
        {
            Version = NetConstants.ProtocolVersion,
            PlayerId = PlayerId,
            Token = Token,
            Name = playerName,
            Ticket = ticket,
        };
    }
}
