using System;
using UnityEngine;

namespace Underworld.Core
{
    public enum LauncherState { Idle, StartingServer, Connecting, InGame, Transferring, Failed }

    /// <summary>
    /// Avvio e connessione. Il menu (UI) chiama questi metodi; la parte di rete li implementa.
    /// Tutti i testi di stato sono in inglese, pronti da mostrare al giocatore.
    /// </summary>
    public interface IGameLauncher
    {
        LauncherState State { get; }
        string StatusText { get; }
        event Action StateChanged;

        /// <summary>Avvia i server sul PC (solo per questo PC) e collega il giocatore.</summary>
        void PlayLocal(string playerName);

        /// <summary>Come PlayLocal, ma i server accettano anche gli amici dalla rete.</summary>
        void HostForFriends(string playerName);

        /// <summary>Collega il giocatore a un server esistente.</summary>
        void Join(string address, ushort port, string playerName);

        /// <summary>Esce dalla partita e torna al menu.</summary>
        void Disconnect();

        /// <summary>Chiude il gioco (e i server avviati da questo client).</summary>
        void QuitGame();
    }

    /// <summary>Punti di accesso ai servizi condivisi tra moduli.</summary>
    public static class GameServices
    {
        public static IGameLauncher Launcher { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Launcher = null;
    }
}
