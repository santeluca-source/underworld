using UnityEngine;

namespace Underworld.Player
{
    /// <summary>Comandi di movimento di un frame.</summary>
    public struct PlayerInputState
    {
        /// <summary>Direzione da -1 a 1: x = destra, y = avanti.</summary>
        public Vector2 Move;
        public bool Sprint;
        /// <summary>Vero solo nel frame in cui il salto viene chiesto.</summary>
        public bool Jump;
        /// <summary>
        /// Falso: Move è relativo alla camera (giocatore umano).
        /// Vero: Move è nel mondo (x = asse X, y = asse Z), comodo per i bot.
        /// </summary>
        public bool WorldSpace;
    }

    /// <summary>
    /// Sorgente dei comandi del personaggio. Il ThirdPersonController la legge una volta per frame.
    /// Implementazioni: KeyboardMouseInput (persone), BotInput (bot e test automatici).
    /// </summary>
    public interface IPlayerInput
    {
        PlayerInputState Read();
    }
}
