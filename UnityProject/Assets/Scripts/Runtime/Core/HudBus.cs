using System;
using UnityEngine;

namespace Underworld.Core
{
    public enum ToastKind { Info, Success, Warning, Error }

    /// <summary>
    /// Canale tra la logica di gioco e l'HUD. La logica scrive qui; la UI ascolta gli eventi.
    /// Così i moduli non dipendono dalla UI e la UI non dipende dai moduli.
    /// Gira solo sul client (sul server headless nessuno ascolta).
    /// </summary>
    public static class HudBus
    {
        public static event Action<string> PromptChanged;          // null = nascondi
        public static event Action<string, ToastKind> ToastRequested;
        public static event Action<long> CashChanged;
        public static event Action<int> WantedChanged;              // 0..5 stelle
        public static event Action<string> ObjectiveChanged;        // null = nascondi
        public static event Action<string> AreaChanged;             // nome dell'area da mostrare (in inglese)

        public static string Prompt { get; private set; }
        public static long Cash { get; private set; }
        public static int Wanted { get; private set; }
        public static string Objective { get; private set; }
        public static string Area { get; private set; }
        /// <summary>Indirizzo "ip:porta" a cui si collegano gli amici quando questo PC ospita la partita (null = non ospita).</summary>
        public static string HostAddress { get; private set; }

        public static void SetPrompt(string text)
        {
            if (text == Prompt) return;
            Prompt = text;
            PromptChanged?.Invoke(text);
        }

        public static void Toast(string text, ToastKind kind = ToastKind.Info) => ToastRequested?.Invoke(text, kind);

        public static void SetCash(long cash)
        {
            if (cash == Cash) return;
            Cash = cash;
            CashChanged?.Invoke(cash);
        }

        public static void SetWanted(int stars)
        {
            stars = Mathf.Clamp(stars, 0, 5);
            if (stars == Wanted) return;
            Wanted = stars;
            WantedChanged?.Invoke(stars);
        }

        public static void SetObjective(string text)
        {
            if (text == Objective) return;
            Objective = text;
            ObjectiveChanged?.Invoke(text);
        }

        public static void SetArea(string displayName)
        {
            if (displayName == Area) return;
            Area = displayName;
            AreaChanged?.Invoke(displayName);
        }

        /// <summary>Indirizzo per gli amici (lo mostra il menu di pausa). null = questo PC non ospita.</summary>
        public static void SetHostAddress(string address) => HostAddress = string.IsNullOrEmpty(address) ? null : address;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            HostAddress = null;
            PromptChanged = null; ToastRequested = null; CashChanged = null;
            WantedChanged = null; ObjectiveChanged = null; AreaChanged = null;
            Prompt = null; Cash = 0; Wanted = 0; Objective = null; Area = null;
        }
    }
}
