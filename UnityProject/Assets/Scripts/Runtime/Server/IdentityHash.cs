using System;
using System.Security.Cryptography;
using System.Text;

namespace Underworld.Server
{
    /// <summary>
    /// Hash dell'identità del client (contratto, sezione 6). Il server salva solo
    /// SHA256(playerId + ":" + token) in esadecimale minuscolo, mai il token.
    /// Il confronto è a tempo costante. Logica pura (test EditMode).
    /// </summary>
    public static class IdentityHash
    {
        /// <summary>Lunghezza dell'hash in caratteri esadecimali (32 byte).</summary>
        public const int HexLength = 64;

        public static string Compute(string playerId, string token)
        {
            if (playerId == null) throw new ArgumentNullException(nameof(playerId));
            if (token == null) throw new ArgumentNullException(nameof(token));
            byte[] bytes = Encoding.UTF8.GetBytes(playerId + ":" + token);
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(HexLength);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Vero se è un hash nel formato giusto (64 caratteri 0-9 a-f).</summary>
        public static bool IsValidHash(string hash)
        {
            if (hash == null || hash.Length != HexLength) return false;
            foreach (char c in hash)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        /// <summary>
        /// Confronto a tempo costante: il tempo dipende solo dalla lunghezza, non da dove le stringhe differiscono.
        /// Con lunghezze diverse (o null) restituisce falso.
        /// </summary>
        public static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            int diff = a.Length ^ b.Length;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>Il token corrisponde all'hash salvato?</summary>
        public static bool Verify(string playerId, string token, string storedHash)
        {
            if (playerId == null || token == null || !IsValidHash(storedHash)) return false;
            return FixedTimeEquals(Compute(playerId, token), storedHash);
        }
    }

    /// <summary>
    /// Controllo del playerId prima di usarlo in un percorso di file: deve essere un GUID nel formato "D" minuscolo
    /// (36 caratteri, 8-4-4-4-12). Niente '..', separatori o nomi riservati possono arrivare al disco.
    /// </summary>
    public static class PlayerIdPath
    {
        public static bool IsValid(string playerId)
        {
            if (playerId == null || playerId.Length != 36) return false;
            if (!Guid.TryParseExact(playerId, "D", out Guid g)) return false;
            // Solo la forma canonica minuscola: un id non può avere due nomi di file diversi.
            return string.Equals(g.ToString("D"), playerId, StringComparison.Ordinal);
        }

        /// <summary>Percorso &lt;folder&gt;/&lt;playerId&gt;&lt;extension&gt;. Lancia ArgumentException se il playerId non è valido.</summary>
        public static string Combine(string folder, string playerId, string extension)
        {
            if (!IsValid(playerId)) throw new ArgumentException($"playerId non valido per un percorso: '{playerId}'", nameof(playerId));
            return System.IO.Path.Combine(folder, playerId + extension);
        }
    }
}
