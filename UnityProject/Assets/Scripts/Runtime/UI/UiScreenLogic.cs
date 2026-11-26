using System;
using System.Collections.Generic;
using Underworld.Core;

namespace Underworld.UI
{
    /// <summary>Schermata di base della UI.</summary>
    public enum UiMode
    {
        /// <summary>Niente UI (es. scena Bootstrap senza connessione in corso).</summary>
        None,
        /// <summary>Menu principale (scena MainMenu). Mostra anche avvio, connessione ed errori.</summary>
        Menu,
        /// <summary>Caricamento a schermo pieno fuori dal menu (connessione diretta, trasferimento tra aree).</summary>
        Loading,
        /// <summary>In gioco: HUD, con la pausa disponibile.</summary>
        Hud,
    }

    /// <summary>Cosa fare quando il giocatore preme Esc.</summary>
    public enum EscapeAction { None, CloseWindow, OpenPause, ClosePause, MenuBack }

    /// <summary>
    /// Regole pure per scegliere la schermata. Nessuna dipendenza da Unity: testate in EditMode.
    /// </summary>
    public static class UiModeRules
    {
        public const string MenuScene = "MainMenu";
        public const string BootstrapScene = "Bootstrap";

        /// <summary>Scene di gioco: le aree (Area_*) e la vecchia scena di prova.</summary>
        public static bool IsGameScene(string scene) =>
            !string.IsNullOrEmpty(scene) && (scene.StartsWith("Area_", StringComparison.Ordinal) || scene == "Downtown_Test");

        /// <summary>Stati del launcher che mostrano un'attesa (avvio, connessione, trasferimento).</summary>
        public static bool IsBusy(LauncherState state) =>
            state == LauncherState.StartingServer || state == LauncherState.Connecting || state == LauncherState.Transferring;

        /// <summary>
        /// Schermata per la scena attiva e lo stato del launcher (null = launcher assente).
        /// InGame vince sempre: in gioco si vede l'HUD.
        /// </summary>
        public static UiMode Resolve(string activeScene, LauncherState? state)
        {
            if (state == LauncherState.InGame) return UiMode.Hud;
            if (activeScene == MenuScene) return UiMode.Menu;
            if (state.HasValue && (IsBusy(state.Value) || state.Value == LauncherState.Failed)) return UiMode.Loading;
            if (IsGameScene(activeScene)) return UiMode.Hud;
            return UiMode.None;
        }
    }

    /// <summary>
    /// Macchina a stati delle schermate: modo attuale, pausa, finestre aperte, tasto Esc.
    /// La UI (UiRoot) le passa scena e stato del launcher; la macchina dice cosa mostrare.
    /// </summary>
    public sealed class UiScreenMachine
    {
        public UiMode Mode { get; private set; } = UiMode.None;
        public bool PauseOpen { get; private set; }

        /// <summary>(vecchio modo, nuovo modo)</summary>
        public event Action<UiMode, UiMode> ModeChanged;
        public event Action<bool> PauseChanged;

        /// <summary>Aggiorna il contesto. Ritorna true se il modo è cambiato.</summary>
        public bool SetContext(string activeScene, LauncherState? state)
        {
            UiMode next = UiModeRules.Resolve(activeScene, state);
            if (next == Mode) return false;
            UiMode old = Mode;
            Mode = next;
            if (next != UiMode.Hud) SetPause(false); // la pausa esiste solo in gioco
            ModeChanged?.Invoke(old, next);
            return true;
        }

        /// <summary>Apre o chiude la pausa. Fuori dall'HUD non si apre.</summary>
        public bool SetPause(bool open)
        {
            if (open && Mode != UiMode.Hud) return false;
            if (PauseOpen == open) return false;
            PauseOpen = open;
            PauseChanged?.Invoke(open);
            return true;
        }

        /// <summary>Decide cosa fa Esc. Prima si chiude la finestra in cima, poi la pausa.</summary>
        public EscapeAction Escape(int openWindows)
        {
            if (openWindows > 0) return EscapeAction.CloseWindow;
            switch (Mode)
            {
                case UiMode.Hud: return PauseOpen ? EscapeAction.ClosePause : EscapeAction.OpenPause;
                case UiMode.Menu: return EscapeAction.MenuBack;
                default: return EscapeAction.None;
            }
        }

        /// <summary>True quando una schermata della UI deve fermare il personaggio e liberare il mouse.</summary>
        public bool BlocksGameplay => Mode == UiMode.Menu || Mode == UiMode.Loading || PauseOpen;
    }

    /// <summary>
    /// Coda degli avvisi (toast): al massimo <see cref="MaxVisible"/> visibili, ognuno con la sua durata.
    /// Logica pura: la parte grafica (ToastStack) la segue. Testata in EditMode.
    /// </summary>
    public sealed class ToastQueue
    {
        public const int MaxVisible = 3;

        public sealed class Entry
        {
            public int Id;
            public string Text;
            public ToastKind Kind;
            public float Remaining;
        }

        readonly List<Entry> _items = new List<Entry>();
        int _nextId = 1;

        public IReadOnlyList<Entry> Items => _items;

        /// <summary>Durata a schermo: gli errori restano di più, i testi lunghi anche.</summary>
        public static float DurationFor(ToastKind kind, string text)
        {
            float baseTime = kind == ToastKind.Error ? 6f : kind == ToastKind.Warning ? 5f : 4f;
            int len = text == null ? 0 : text.Length;
            return baseTime + Math.Min(3f, Math.Max(0, len - 40) * 0.04f);
        }

        /// <summary>Aggiunge un avviso in cima. Se sono troppi, il più vecchio esce (ritornato in <paramref name="dropped"/>).</summary>
        public Entry Push(string text, ToastKind kind, out Entry dropped)
        {
            dropped = null;
            var e = new Entry { Id = _nextId++, Text = text ?? string.Empty, Kind = kind, Remaining = DurationFor(kind, text) };
            _items.Insert(0, e);
            if (_items.Count > MaxVisible)
            {
                dropped = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
            }
            return e;
        }

        /// <summary>Fa passare il tempo. Gli avvisi scaduti finiscono in <paramref name="expired"/>.</summary>
        public void Tick(float dt, List<Entry> expired)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                _items[i].Remaining -= dt;
                if (_items[i].Remaining <= 0f)
                {
                    expired?.Add(_items[i]);
                    _items.RemoveAt(i);
                }
            }
        }

        public void Clear() => _items.Clear();
    }
}
