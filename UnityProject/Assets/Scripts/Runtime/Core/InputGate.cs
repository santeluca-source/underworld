using System;
using System.Collections.Generic;
using UnityEngine;

namespace Underworld.Core
{
    /// <summary>
    /// Blocca i comandi di gioco (movimento, camera, interazioni) quando una finestra UI è aperta.
    /// Chi apre una finestra chiama Block(this); quando la chiude chiama Unblock(this).
    /// Il controller e la camera leggono GameplayBlocked. Con il blocco attivo il mouse è libero.
    /// </summary>
    public static class InputGate
    {
        static readonly HashSet<object> Blockers = new HashSet<object>();

        public static event Action Changed;

        public static bool GameplayBlocked => Blockers.Count > 0;

        public static void Block(object reason)
        {
            if (reason != null && Blockers.Add(reason)) Changed?.Invoke();
        }

        public static void Unblock(object reason)
        {
            if (reason != null && Blockers.Remove(reason)) Changed?.Invoke();
        }

        public static void Clear()
        {
            if (Blockers.Count == 0) return;
            Blockers.Clear();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Blockers.Clear();
            Changed = null;
        }
    }
}
