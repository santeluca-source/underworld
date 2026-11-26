namespace Underworld.Net
{
    /// <summary>
    /// Motivi di rifiuto e di disconnessione che il server manda al client.
    /// Formato: "codice|testo". Il codice serve al programma (e ai test), il testo è in italiano
    /// ed è pronto da mostrare al giocatore. Logica pura (testata in EditMode).
    /// </summary>
    public static class DisconnectReasons
    {
        public const char Separator = '|';

        // ---- Codici (stabili: i test e la UI li usano) ----
        public const string ProtocolVersionCode = "protocol_version";
        public const string PayloadTooLargeCode = "payload_too_large";
        public const string PayloadInvalidCode = "payload_invalid";
        public const string IdentityInvalidCode = "identity_invalid";
        public const string NameInvalidCode = "name_invalid";
        public const string ServerFullCode = "server_full";
        public const string AlreadyConnectedCode = "already_connected";
        public const string ServerStartingCode = "server_starting";
        public const string ServerClosingCode = "server_closing";
        public const string MovementKickCode = "movement_invalid";
        public const string InternalErrorCode = "internal_error";

        /// <summary>Unisce codice e testo nel formato di rete.</summary>
        public static string Format(string code, string text) => code + Separator + text;

        /// <summary>
        /// Divide un motivo ricevuto in codice e testo.
        /// Falso se il motivo non è nel nostro formato (per esempio un messaggio interno di Netcode).
        /// </summary>
        public static bool TryParse(string raw, out string code, out string text)
        {
            code = null;
            text = null;
            if (string.IsNullOrEmpty(raw)) return false;
            int i = raw.IndexOf(Separator);
            if (i <= 0 || i >= raw.Length - 1) return false;
            code = raw.Substring(0, i);
            for (int k = 0; k < code.Length; k++)
            {
                char c = code[k];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) { code = null; return false; }
            }
            text = raw.Substring(i + 1);
            return true;
        }

        // ---- Motivi pronti ----

        public static string ProtocolVersion(int client, int server) => Format(ProtocolVersionCode,
            $"Versione del gioco diversa dal server (tua v{client}, server v{server}). Aggiorna il gioco.");

        public static string PayloadTooLarge(int bytes, int max) => Format(PayloadTooLargeCode,
            $"Dati di connessione troppo grandi ({bytes} byte, massimo {max}).");

        public static readonly string PayloadInvalid = Format(PayloadInvalidCode, "Dati di connessione non validi.");

        public static readonly string IdentityInvalid = Format(IdentityInvalidCode, "Identità del giocatore non valida.");

        public static readonly string NameInvalid = Format(NameInvalidCode, "Nome non valido: " + PlayerNameRules.RuleText + ".");

        public static string ServerFull(int current, int max) => Format(ServerFullCode,
            $"Server pieno ({current}/{max}). Riprova più tardi.");

        public static readonly string AlreadyConnected = Format(AlreadyConnectedCode,
            "Questo giocatore è già collegato al server.");

        public static readonly string ServerStarting = Format(ServerStartingCode,
            "Il server si sta avviando. Riprova tra poco.");

        public static readonly string ServerClosing = Format(ServerClosingCode, "Il server si è chiuso.");

        public static readonly string MovementKick = Format(MovementKickCode,
            "Disconnesso dal server: movimento non valido.");

        public static readonly string InternalError = Format(InternalErrorCode, "Errore interno del server.");
    }
}
