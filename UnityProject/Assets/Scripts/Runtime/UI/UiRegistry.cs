using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Underworld.Core;

namespace Underworld.UI
{
    /// <summary>Zone dell'HUD dove un modulo può mettere i suoi elementi.</summary>
    public enum HudSlot
    {
        /// <summary>In alto a sinistra, sotto l'obiettivo.</summary>
        TopLeft,
        /// <summary>In alto a destra, sotto soldi, stelle e avvisi.</summary>
        TopRight,
        /// <summary>In basso a sinistra (es. chat, voce).</summary>
        BottomLeft,
        /// <summary>In basso a destra (es. veicolo, munizioni).</summary>
        BottomRight,
        /// <summary>In basso al centro, sopra il suggerimento "E".</summary>
        BottomCenter,
    }

    /// <summary>Elemento dell'HUD fornito da un modulo.</summary>
    public sealed class HudWidget
    {
        /// <summary>Id unico (es. "vehicles.speed").</summary>
        public string Id;
        public HudSlot Slot;
        /// <summary>Ordine nella zona (più basso = prima).</summary>
        public int Order;
        /// <summary>Crea l'elemento. Chiamata una volta, quando l'HUD esiste.</summary>
        public Func<VisualElement> Create;

        /// <summary>Elemento creato (null finché l'HUD non c'è).</summary>
        public VisualElement Element { get; internal set; }
    }

    /// <summary>
    /// Registro per i moduli: finestre, elementi dell'HUD e fogli di stile aggiuntivi.
    /// Un modulo si registra dal suo codice (es. in un metodo [RuntimeInitializeOnLoadMethod]) e non modifica i file della UI.
    /// Le registrazioni valgono anche prima che la UI esista: la UI le applica quando nasce.
    /// Sul server headless la UI non nasce: registrare non costa niente.
    /// </summary>
    public static class UiRegistry
    {
        static readonly Dictionary<string, Func<UiWindow>> WindowFactories = new Dictionary<string, Func<UiWindow>>();
        static readonly Dictionary<string, UiWindow> WindowInstances = new Dictionary<string, UiWindow>();
        static readonly List<HudWidget> Widgets = new List<HudWidget>();
        static readonly List<StyleSheet> Sheets = new List<StyleSheet>();

        internal static event Action<HudWidget> WidgetAdded;
        internal static event Action<HudWidget> WidgetRemoved;
        internal static event Action<StyleSheet> SheetAdded;

        public static IReadOnlyList<HudWidget> HudWidgets => Widgets;
        public static IReadOnlyList<StyleSheet> StyleSheets => Sheets;

        // ---------- Finestre ----------

        /// <summary>Registra una finestra con una fabbrica. La finestra si crea alla prima apertura.</summary>
        public static void RegisterWindow(string id, Func<UiWindow> factory)
        {
            if (string.IsNullOrEmpty(id) || factory == null) throw new ArgumentException("Id e fabbrica sono obbligatori");
            WindowFactories[id] = factory;
            WindowInstances.Remove(id);
        }

        public static void UnregisterWindow(string id)
        {
            if (id == null) return;
            if (WindowInstances.TryGetValue(id, out var w) && w.IsOpen) UiWindows.Close(w);
            WindowFactories.Remove(id);
            WindowInstances.Remove(id);
        }

        public static bool HasWindow(string id) => id != null && WindowFactories.ContainsKey(id);

        /// <summary>Istanza della finestra (creata la prima volta). Null se l'id non è registrato.</summary>
        public static UiWindow GetWindow(string id)
        {
            if (id == null) return null;
            if (WindowInstances.TryGetValue(id, out var w) && w != null) return w;
            if (!WindowFactories.TryGetValue(id, out var factory)) return null;
            w = factory();
            if (w == null) return null;
            w.WindowId = id;
            WindowInstances[id] = w;
            return w;
        }

        // ---------- HUD ----------

        /// <summary>Aggiunge un elemento all'HUD. Se c'è già un elemento con lo stesso id, lo sostituisce.</summary>
        public static HudWidget RegisterHudWidget(string id, HudSlot slot, Func<VisualElement> create, int order = 0)
        {
            if (string.IsNullOrEmpty(id) || create == null) throw new ArgumentException("Id e fabbrica sono obbligatori");
            UnregisterHudWidget(id);
            var w = new HudWidget { Id = id, Slot = slot, Create = create, Order = order };
            Widgets.Add(w);
            WidgetAdded?.Invoke(w);
            return w;
        }

        public static void UnregisterHudWidget(string id)
        {
            for (int i = Widgets.Count - 1; i >= 0; i--)
            {
                if (Widgets[i].Id != id) continue;
                var w = Widgets[i];
                Widgets.RemoveAt(i);
                WidgetRemoved?.Invoke(w);
            }
        }

        // ---------- Stili ----------

        /// <summary>Aggiunge un foglio USS del modulo (caricato dal modulo, es. con Resources.Load).</summary>
        public static void RegisterStyleSheet(StyleSheet sheet)
        {
            if (sheet == null || Sheets.Contains(sheet)) return;
            Sheets.Add(sheet);
            SheetAdded?.Invoke(sheet);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            WindowFactories.Clear();
            WindowInstances.Clear();
            Widgets.Clear();
            Sheets.Clear();
            WidgetAdded = null;
            WidgetRemoved = null;
            SheetAdded = null;
        }
    }

    /// <summary>
    /// Finestre aperte: apri, chiudi, chiudi quella in cima. Funziona solo sul client (con UiRoot).
    /// </summary>
    public static class UiWindows
    {
        static UiWindowStack Stack => UiRoot.Instance != null ? UiRoot.Instance.Windows : null;

        /// <summary>Numero di finestre aperte.</summary>
        public static int OpenCount => Stack != null ? Stack.Count : 0;

        public static bool IsOpen(string id)
        {
            var w = id != null ? UiRegistry.GetWindow(id) : null;
            return w != null && w.IsOpen;
        }

        /// <summary>Apre una finestra registrata. Ritorna la finestra (o null se l'id non esiste o non c'è UI).</summary>
        public static UiWindow Open(string id)
        {
            var w = UiRegistry.GetWindow(id);
            if (w == null)
            {
                Debug.LogWarning($"[UI] Finestra non registrata: {id}");
                return null;
            }
            return Open(w);
        }

        public static UiWindow Open(UiWindow window)
        {
            var s = Stack;
            if (s == null || window == null) return null;
            s.Open(window);
            return window;
        }

        public static void Close(string id)
        {
            var w = UiRegistry.GetWindow(id);
            if (w != null) Close(w);
        }

        public static void Close(UiWindow window) => Stack?.Close(window);

        public static void Toggle(string id)
        {
            if (IsOpen(id)) Close(id); else Open(id);
        }

        public static void CloseAll() => Stack?.CloseAll();
    }

    /// <summary>Pila delle finestre aperte, con lo sfondo scuro condiviso. Vive dentro UiRoot.</summary>
    public sealed class UiWindowStack
    {
        readonly VisualElement _layer;
        readonly VisualElement _scrim;
        readonly List<UiWindow> _open = new List<UiWindow>();

        public int Count => _open.Count;
        public UiWindow Top => _open.Count > 0 ? _open[_open.Count - 1] : null;

        /// <summary>Cambia quando si apre o si chiude una finestra.</summary>
        public event Action Changed;

        public UiWindowStack(VisualElement layer)
        {
            _layer = layer;
            _scrim = UiBuild.Box("uw-scrim");
            _scrim.style.display = DisplayStyle.None;
            _scrim.RegisterCallback<PointerDownEvent>(_ =>
            {
                var top = Top;
                if (top != null && top.CloseOnEscape) Close(top);
            });
            _layer.Add(_scrim);
        }

        public void Open(UiWindow w)
        {
            if (_open.Contains(w))
            {
                // Già aperta: portala in cima.
                _open.Remove(w);
                _open.Add(w);
                w.BringToFront();
                Changed?.Invoke();
                return;
            }
            _open.Add(w);
            if (w.parent != _layer) _layer.Add(w);
            w.BringToFront();
            w.IsOpen = true;
            if (w.BlocksGameplay) InputGate.Block(w);
            UpdateScrim();
            w.PlayOpen();
            w.RaiseOpened();
            Changed?.Invoke();
        }

        public void Close(UiWindow w)
        {
            if (w == null || !_open.Remove(w)) return;
            w.IsOpen = false;
            InputGate.Unblock(w);
            UpdateScrim();
            w.RaiseClosed();
            Changed?.Invoke();
            w.PlayClose(() =>
            {
                if (!w.IsOpen) w.RemoveFromHierarchy();
            });
        }

        /// <summary>Chiude la finestra in cima che accetta Esc. Ritorna true se ne ha chiusa una.</summary>
        public bool CloseTop()
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                if (!_open[i].CloseOnEscape) continue;
                Close(_open[i]);
                return true;
            }
            return false;
        }

        public void CloseAll()
        {
            for (int i = _open.Count - 1; i >= 0; i--) Close(_open[i]);
        }

        bool _scrimVisible;

        void UpdateScrim()
        {
            bool any = false;
            foreach (var w in _open) any |= w.ShowScrim;
            if (any)
            {
                // Lo sfondo scuro sta subito sotto la finestra in cima.
                var top = Top;
                if (top != null) _scrim.PlaceBehind(top);
                if (!_scrimVisible) UiAnim.In(_scrim, UiAnim.EnterTime);
            }
            else if (_scrimVisible)
            {
                UiAnim.Out(_scrim, UiAnim.ExitTime(UiAnim.EnterTime));
            }
            _scrimVisible = any;
        }
    }
}
