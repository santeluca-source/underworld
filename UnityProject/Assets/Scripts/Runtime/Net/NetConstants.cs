namespace Underworld.Net
{
    /// <summary>
    /// Valori fissi della rete. Vedi Docs/ARCHITECTURE.md, sezione 4.
    /// </summary>
    public static class NetConstants
    {
        /// <summary>
        /// Versione del protocollo di gioco (campo "v" del ConnectionPayload).
        /// Aumentala quando client e server vecchi non possono più giocare insieme.
        /// </summary>
        public const int ProtocolVersion = 1;

        /// <summary>Aggiornamenti di rete al secondo.</summary>
        public const uint TickRate = 30;

        /// <summary>Giocatori massimi per server (l'argomento -maxplayers lo cambia, per i test).</summary>
        public const int DefaultMaxPlayers = 16;

        /// <summary>Dimensione massima dei dati di connessione (byte). Oltre, il server rifiuta.</summary>
        public const int MaxPayloadBytes = 2048;

        /// <summary>Lunghezza massima del biglietto di trasferimento (Fase 11).</summary>
        public const int MaxTicketLength = 512;

        /// <summary>Prefisso delle righe di log del server (gli strumenti di test lo cercano).</summary>
        public const string ServerLog = "[Server]";

        /// <summary>Prefisso delle righe di log del client.</summary>
        public const string ClientLog = "[Client]";

        /// <summary>Riga di log che dice "il server accetta i giocatori". Tools/test.ps1 la aspetta.</summary>
        public const string ServerReadyMarker = "[Server] Pronto";

        /// <summary>Nome della scena del menu (la crea il modulo UI).</summary>
        public const string MainMenuScene = "MainMenu";

        /// <summary>
        /// File scritto dal server nella sua cartella dati quando è pronto (JSON con pid, area, porta).
        /// Lo leggono GameLauncher (Gioca) e Tools/test.ps1.
        /// </summary>
        public const string ReadyFileName = "server.ready";

        /// <summary>
        /// Se questo file compare nella cartella dati, il server si chiude in modo pulito
        /// (salva e scollega i giocatori). Lo usano Tools/test.ps1 e GameLauncher.
        /// </summary>
        public const string StopFileName = "server.stop";

        /// <summary>Nome della scena di avvio (indice 0 della build).</summary>
        public const string BootstrapScene = "Bootstrap";
    }
}
