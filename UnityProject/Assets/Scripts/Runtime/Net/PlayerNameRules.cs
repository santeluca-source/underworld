using System.Text;

namespace Underworld.Net
{
    /// <summary>
    /// Regola del nome visibile: da 3 a 16 caratteri; lettere, numeri, spazio, "_" e "-".
    /// Il nome si normalizza prima del controllo: niente spazi all'inizio e alla fine,
    /// più spazi di fila diventano uno solo. Logica pura (testata in EditMode).
    /// </summary>
    public static class PlayerNameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        /// <summary>Testo da mostrare quando il nome non va bene.</summary>
        public const string RuleText = "usa da 3 a 16 caratteri (lettere, numeri, spazio, _ e -)";

        /// <summary>Toglie gli spazi ai bordi e unisce gli spazi ripetuti. Null diventa stringa vuota.</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(raw.Length);
            bool pendingSpace = false;
            foreach (char c in raw)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace) sb.Append(' ');
                pendingSpace = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Carattere permesso nel nome.</summary>
        public static bool IsAllowedChar(char c) =>
            char.IsLetter(c) || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '-';

        /// <summary>Vero se il nome (già normalizzato) rispetta la regola.</summary>
        public static bool IsValid(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Length < MinLength || name.Length > MaxLength) return false;
            if (name[0] == ' ' || name[name.Length - 1] == ' ') return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!IsAllowedChar(c)) return false;
                if (c == ' ' && i > 0 && name[i - 1] == ' ') return false;
            }
            return true;
        }

        /// <summary>Normalizza e controlla. Restituisce falso se il nome non va bene.</summary>
        public static bool TryNormalize(string raw, out string normalized)
        {
            normalized = Normalize(raw);
            return IsValid(normalized);
        }
    }
}
