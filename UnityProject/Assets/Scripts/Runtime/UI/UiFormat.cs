using System;

namespace Underworld.UI
{
    /// <summary>
    /// Formati dei testi della UI. Logica pura, senza Unity: è coperta dai test EditMode (UiFormatTests).
    /// </summary>
    public static class UiFormat
    {
        /// <summary>Simbolo della valuta del gioco. Va prima del numero, come in inglese (US): "$12,500".</summary>
        public const string Currency = "$";

        /// <summary>Numero intero con la virgola delle migliaia (inglese US): 12500 → "12,500", -5000 → "-5,000".</summary>
        public static string Thousands(long value)
        {
            if (value == 0) return "0";
            bool negative = value < 0;
            // long.MinValue non ha l'opposto in long: lavoro in ulong.
            ulong v = negative ? (ulong)(-(value + 1)) + 1UL : (ulong)value;

            Span<char> buf = stackalloc char[32];
            int pos = buf.Length;
            int digits = 0;
            while (v > 0)
            {
                if (digits > 0 && digits % 3 == 0) buf[--pos] = ',';
                buf[--pos] = (char)('0' + (int)(v % 10));
                v /= 10;
                digits++;
            }
            if (negative) buf[--pos] = '-';
            return new string(buf.Slice(pos));
        }

        /// <summary>Soldi in formato inglese (US): 12500 → "$12,500", -300 → "-$300".</summary>
        public static string Money(long value) =>
            value < 0 ? "-" + Currency + Thousands(value).Substring(1) : Currency + Thousands(value);

        /// <summary>Differenza di soldi con il segno: 2500 → "+$2,500", -300 → "-$300".</summary>
        public static string MoneyDelta(long delta) => (delta > 0 ? "+" : "") + Money(delta);
    }

    /// <summary>Esito del controllo del nome del giocatore.</summary>
    public enum NameCheck { Ok, Empty, TooShort, TooLong, InvalidChar }

    /// <summary>
    /// Regole del nome visibile (contratto, sezione 5): da 3 a 16 caratteri,
    /// lettere, numeri, spazio, "_" e "-". Gli spazi all'inizio e alla fine si tolgono.
    /// Il server fa lo stesso controllo: la UI lo ripete solo per dare un aiuto immediato.
    /// </summary>
    public static class PlayerNameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        /// <summary>Nome pulito con la stessa regola del server (spazi ai bordi tolti, spazi ripetuti uniti).</summary>
        public static string Normalize(string raw) => Underworld.Net.PlayerNameRules.Normalize(raw);

        /// <summary>Stessi caratteri del server (cifre solo 0-9: le cifre Unicode il server le rifiuta).</summary>
        public static bool IsAllowedChar(char c) => Underworld.Net.PlayerNameRules.IsAllowedChar(c);

        public static NameCheck Check(string raw)
        {
            string name = Normalize(raw);
            if (name.Length == 0) return NameCheck.Empty;
            foreach (char c in name)
                if (!IsAllowedChar(c)) return NameCheck.InvalidChar;
            if (name.Length < MinLength) return NameCheck.TooShort;
            if (name.Length > MaxLength) return NameCheck.TooLong;
            return NameCheck.Ok;
        }

        public static bool IsValid(string raw) => Check(raw) == NameCheck.Ok;

        /// <summary>Messaggio per il giocatore (frasi brevi, senza punto finale).</summary>
        public static string Message(NameCheck check) => check switch
        {
            NameCheck.Ok => "Other players will see this name",
            NameCheck.Empty => "Choose a name to play",
            NameCheck.TooShort => "Use at least 3 characters",
            NameCheck.TooLong => "Use at most 16 characters",
            NameCheck.InvalidChar => "Use only letters, numbers, space, _ and -",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Lettura dell'indirizzo per "Unisciti". Accetta "ip" + porta, oppure "ip:porta" nel primo campo.
    /// Unity Transport vuole un indirizzo IP: i nomi (tranne "localhost") non sono accettati.
    /// </summary>
    public static class JoinAddress
    {
        public const ushort DefaultPort = 7777;

        public static bool TryParse(string addressInput, string portInput, out string ip, out ushort port, out string error)
        {
            ip = null;
            port = 0;
            error = null;

            string address = addressInput == null ? string.Empty : addressInput.Trim();
            string portText = portInput == null ? string.Empty : portInput.Trim();

            if (address.Length == 0)
            {
                error = "Enter the server IP address";
                return false;
            }

            // "192.168.1.20:7778": la porta scritta qui vince sul campo porta.
            int colon = address.IndexOf(':');
            if (colon >= 0)
            {
                if (address.IndexOf(':', colon + 1) >= 0)
                {
                    error = "IPv6 addresses are not supported yet";
                    return false;
                }
                portText = address.Substring(colon + 1).Trim();
                address = address.Substring(0, colon).Trim();
            }

            if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase)) address = "127.0.0.1";

            if (!IsIPv4(address))
            {
                error = "Invalid address. Example: 192.168.1.20";
                return false;
            }

            if (portText.Length == 0) port = DefaultPort;
            else if (!ushort.TryParse(portText, out port) || port == 0)
            {
                error = "Invalid port: use a number from 1 to 65535";
                return false;
            }

            ip = address;
            return true;
        }

        /// <summary>Quattro numeri da 0 a 255 separati da punti, senza spazi.</summary>
        public static bool IsIPv4(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int parts = 0, digits = 0, value = 0;
            for (int i = 0; i <= s.Length; i++)
            {
                if (i == s.Length || s[i] == '.')
                {
                    if (digits == 0 || value > 255) return false;
                    parts++;
                    digits = 0;
                    value = 0;
                    continue;
                }
                char c = s[i];
                if (c < '0' || c > '9' || digits == 3) return false;
                value = value * 10 + (c - '0');
                digits++;
            }
            return parts == 4;
        }
    }
}
