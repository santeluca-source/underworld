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
            $"Your game version does not match the server (you v{client}, server v{server}). Please update the game.");

        public static string PayloadTooLarge(int bytes, int max) => Format(PayloadTooLargeCode,
            $"Connection data is too large ({bytes} bytes, max {max}).");

        public static readonly string PayloadInvalid = Format(PayloadInvalidCode, "Invalid connection data.");

        public static readonly string IdentityInvalid = Format(IdentityInvalidCode, "Invalid player identity.");

        public static readonly string NameInvalid = Format(NameInvalidCode, "Invalid name: " + PlayerNameRules.RuleText + ".");

        public static string ServerFull(int current, int max) => Format(ServerFullCode,
            $"Server is full ({current}/{max}). Try again later.");

        public static readonly string AlreadyConnected = Format(AlreadyConnectedCode,
            "This player is already connected to the server.");

        public static readonly string ServerStarting = Format(ServerStartingCode,
            "The server is starting. Try again in a moment.");

        public static readonly string ServerClosing = Format(ServerClosingCode, "The server has closed.");

        public static readonly string MovementKick = Format(MovementKickCode,
            "Disconnected from the server: invalid movement.");

        public static readonly string InternalError = Format(InternalErrorCode, "Internal server error.");
    }
}
