using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Underworld.Core;

namespace Underworld.UI
{
    /// <summary>
    /// HUD in gioco, guidato solo da HudBus: soldi (in alto a destra), stelle ricercato, nome dell'area,
    /// suggerimento "E", obiettivo (in alto a sinistra). HUD quieto: niente vetro dietro ogni elemento,
    /// solo un'ombra sul testo per leggerlo anche di giorno. I moduli aggiungono elementi con UiRegistry.
    /// </summary>
    public sealed class HudScreen
    {
        public VisualElement Root { get; }

        readonly Dictionary<HudSlot, VisualElement> _slots = new Dictionary<HudSlot, VisualElement>();
        readonly VisualElement _rightColumn;

        // Soldi
        readonly VisualElement _cashRow;
        readonly Label _cashValue;
        readonly Label _cashDelta;
        long _cashTarget;
        double _cashShown;
        long _cashLastText = long.MinValue;
        float _deltaHold;

        // Ricercato
        readonly VisualElement _wanted;
        readonly UiIcon[] _stars = new UiIcon[5];
        int _wantedShown;

        // Area
        readonly VisualElement _area;
        readonly Label _areaName;
        float _areaHold;

        // Suggerimento
        readonly VisualElement _prompt;
        readonly Label _promptText;
        bool _promptVisible;

        // Obiettivo
        readonly VisualElement _objective;
        readonly Label _objectiveText;
        bool _objectiveVisible;

        bool _visible;
        bool _subscribed;

        /// <summary>Contenitore dove UiRoot mette gli avvisi quando l'HUD è attivo.</summary>
        public VisualElement ToastHost { get; }

        public HudScreen()
        {
            Root = UiBuild.Layer("hud");
            Root.AddToClassList("uw-hud");
            Root.style.display = DisplayStyle.None;

            // ---- In alto a sinistra: obiettivo + zona per i moduli
            var left = UiBuild.Box("uw-hud__left", PickingMode.Ignore);
            _objective = UiBuild.Box("uw-objective", PickingMode.Ignore);
            var target = new UiIcon(IconKind.Target, 16f) { Stroke = 2f };
            target.AddToClassList("uw-objective__icon");
            _objectiveText = UiBuild.Text(string.Empty, "uw-objective__text", UiClasses.Shadowed);
            _objective.Add(target);
            _objective.Add(_objectiveText);
            _objective.style.display = DisplayStyle.None;
            left.Add(_objective);
            left.Add(Slot(HudSlot.TopLeft, "uw-hud__slot--top-left"));
            Root.Add(left);

            // ---- In alto a destra: soldi, stelle, avvisi, zona per i moduli
            _rightColumn = UiBuild.Box("uw-hud__right", PickingMode.Ignore);
            _cashRow = UiBuild.Box("uw-cash", PickingMode.Ignore);
            _cashValue = UiBuild.Text("0", "uw-cash__value", UiClasses.Tabular, UiClasses.Shadowed);
            var currency = UiBuild.Text(UiFormat.Currency, "uw-cash__currency", UiClasses.Shadowed);
            _cashRow.Add(_cashValue);
            _cashRow.Add(currency);
            _rightColumn.Add(_cashRow);

            _cashDelta = UiBuild.Text(string.Empty, "uw-cash__delta", UiClasses.Tabular, UiClasses.Shadowed);
            _cashDelta.style.display = DisplayStyle.None;
            _rightColumn.Add(_cashDelta);

            _wanted = UiBuild.Box("uw-wanted", PickingMode.Ignore);
            for (int i = 0; i < _stars.Length; i++)
            {
                var s = new UiIcon(IconKind.Star, 22f);
                s.AddToClassList("uw-wanted__star");
                _stars[i] = s;
                _wanted.Add(s);
            }
            _wanted.style.display = DisplayStyle.None;
            _rightColumn.Add(_wanted);

            ToastHost = UiBuild.Box("uw-hud__toasts", PickingMode.Ignore);
            _rightColumn.Add(ToastHost);
            _rightColumn.Add(Slot(HudSlot.TopRight, "uw-hud__slot--top-right"));
            Root.Add(_rightColumn);

            // ---- In alto al centro: nome dell'area quando cambia
            _area = UiBuild.Box("uw-area", PickingMode.Ignore);
            _areaName = UiBuild.Text(string.Empty, "uw-area__name", UiClasses.Shadowed);
            _area.Add(_areaName);
            _area.Add(UiBuild.Box("uw-area__rule", PickingMode.Ignore));
            _area.style.display = DisplayStyle.None;
            Root.Add(_area);

            // ---- In basso: zone dei moduli e suggerimento "E"
            Root.Add(Slot(HudSlot.BottomLeft, "uw-hud__slot--bottom-left"));
            Root.Add(Slot(HudSlot.BottomRight, "uw-hud__slot--bottom-right"));
            var bottom = UiBuild.Box("uw-hud__bottom", PickingMode.Ignore);
            bottom.Add(Slot(HudSlot.BottomCenter, "uw-hud__slot--bottom-center"));
            _prompt = UiBuild.Box("uw-prompt", PickingMode.Ignore);
            var key = UiBuild.Box("uw-keycap", PickingMode.Ignore);
            key.Add(UiBuild.Text("E", "uw-keycap__label"));
            _promptText = UiBuild.Text(string.Empty, "uw-prompt__text", UiClasses.Shadowed);
            _prompt.Add(key);
            _prompt.Add(_promptText);
            _prompt.style.display = DisplayStyle.None;
            bottom.Add(_prompt);
            Root.Add(bottom);

            // Elementi dei moduli già registrati.
            foreach (var w in UiRegistry.HudWidgets) AddWidget(w);
            UiRegistry.WidgetAdded += AddWidget;
            UiRegistry.WidgetRemoved += RemoveWidget;

            Subscribe();
            ApplyInitialState();
        }

        VisualElement Slot(HudSlot slot, string className)
        {
            var v = UiBuild.Box("uw-hud__slot", PickingMode.Ignore);
            v.AddToClassList(className);
            v.name = "slot-" + slot;
            _slots[slot] = v;
            return v;
        }

        // ------------------------------------------------------------------ moduli

        void AddWidget(HudWidget w)
        {
            if (w == null || w.Create == null || !_slots.TryGetValue(w.Slot, out var slot)) return;
            VisualElement el;
            try { el = w.Create(); }
            catch (System.Exception e) { Debug.LogException(e); return; }
            if (el == null) return;
            el.userData = w;
            w.Element = el;
            // Inserisci in ordine.
            int index = slot.childCount;
            for (int i = 0; i < slot.childCount; i++)
            {
                if (slot[i].userData is HudWidget other && other.Order > w.Order) { index = i; break; }
            }
            slot.Insert(index, el);
        }

        void RemoveWidget(HudWidget w)
        {
            if (w?.Element == null) return;
            w.Element.RemoveFromHierarchy();
            w.Element = null;
        }

        // ------------------------------------------------------------------ HudBus

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            HudBus.CashChanged += OnCash;
            HudBus.WantedChanged += OnWanted;
            HudBus.AreaChanged += OnArea;
            HudBus.PromptChanged += OnPrompt;
            HudBus.ObjectiveChanged += OnObjective;
        }

        /// <summary>Stacca gli eventi (UiRoot lo chiama alla distruzione).</summary>
        public void Dispose()
        {
            if (_subscribed)
            {
                HudBus.CashChanged -= OnCash;
                HudBus.WantedChanged -= OnWanted;
                HudBus.AreaChanged -= OnArea;
                HudBus.PromptChanged -= OnPrompt;
                HudBus.ObjectiveChanged -= OnObjective;
                _subscribed = false;
            }
            UiRegistry.WidgetAdded -= AddWidget;
            UiRegistry.WidgetRemoved -= RemoveWidget;
        }

        void ApplyInitialState()
        {
            _cashTarget = HudBus.Cash;
            _cashShown = _cashTarget;
            SetCashText(_cashTarget);
            SetStars(HudBus.Wanted, false);
            if (HudBus.Objective != null) { _objectiveText.text = HudBus.Objective; _objectiveVisible = true; UiAnim.Set(_objective, true); }
            if (HudBus.Prompt != null) { _promptText.text = CleanPrompt(HudBus.Prompt); _promptVisible = true; UiAnim.Set(_prompt, true); }
        }

        void OnCash(long cash)
        {
            long previous = _cashTarget;
            _cashTarget = cash;
            long delta = cash - previous;
            if (delta == 0) return;
            if (!_visible)
            {
                // Fuori dall'HUD niente animazione: si vede subito il valore giusto.
                _cashShown = cash;
                SetCashText(cash);
                return;
            }

            double from = _cashShown;
            // Il numero "rotola" verso il nuovo valore (ease-out, più lungo per i salti grandi).
            float duration = Mathf.Clamp(0.45f + Mathf.Log10(Mathf.Abs((float)delta) + 1f) * 0.06f, 0.45f, 0.9f);
            UiAnim.Value(_cashValue, duration, Ease.OutQuart, k =>
            {
                _cashShown = from + (cash - from) * k;
                SetCashText((long)System.Math.Round(_cashShown));
            });

            // Pulse breve + colore: verde se entra, rosso se esce.
            _cashRow.EnableInClassList("uw-cash--gain", delta > 0);
            _cashRow.EnableInClassList("uw-cash--loss", delta < 0);
            UiAnim.Pulse(_cashRow, 1.06f, 0.26f);
            _cashDelta.text = UiFormat.MoneyDelta(delta);
            _cashDelta.EnableInClassList("uw-cash__delta--loss", delta < 0);
            UiAnim.In(_cashDelta, 0.2f, 0f, new Vector2(0f, -6f));
            _deltaHold = 1.6f;
        }

        void SetCashText(long value)
        {
            if (value == _cashLastText) return;
            _cashLastText = value;
            _cashValue.text = UiFormat.Thousands(value);
        }

        void OnWanted(int stars) => SetStars(stars, _visible);

        void SetStars(int stars, bool animate)
        {
            stars = Mathf.Clamp(stars, 0, 5);
            int before = _wantedShown;
            _wantedShown = stars;
            for (int i = 0; i < _stars.Length; i++)
            {
                bool on = i < stars;
                _stars[i].Filled = on;
                _stars[i].EnableInClassList("uw-wanted__star--on", on);
                if (animate && on && i >= before) UiAnim.Pulse(_stars[i], 1.3f, 0.3f);
            }
            if (stars > 0 && before == 0)
            {
                if (animate) UiAnim.In(_wanted, 0.2f, 0f, new Vector2(0f, -4f));
                else UiAnim.Set(_wanted, true);
            }
            else if (stars == 0 && before > 0)
            {
                if (animate) UiAnim.Out(_wanted, 0.18f);
                else UiAnim.Set(_wanted, false);
            }
            else if (stars == 0) UiAnim.Set(_wanted, false);
        }

        void OnArea(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return;
            _areaName.text = displayName;
            if (_visible) ShowAreaBanner();
        }

        void ShowAreaBanner()
        {
            if (string.IsNullOrEmpty(_areaName.text)) return;
            UiAnim.In(_area, 0.25f, 0f, new Vector2(0f, 8f));
            _areaHold = 2.8f;
        }

        void OnPrompt(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (_promptVisible) UiAnim.Out(_prompt, 0.14f, new Vector2(0f, 4f));
                _promptVisible = false;
                return;
            }
            _promptText.text = CleanPrompt(text);
            if (!_promptVisible) UiAnim.In(_prompt, 0.18f, 0f, new Vector2(0f, 6f));
            _promptVisible = true;
        }

        /// <summary>Il suggerimento arriva come "testo". Se un modulo scrive già "E — testo", tolgo il tasto.</summary>
        public static string CleanPrompt(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string t = text.Trim();
            string[] prefixes = { "[E]", "E —", "E –", "E -", "E:" };
            foreach (var p in prefixes)
                if (t.StartsWith(p, System.StringComparison.Ordinal)) return t.Substring(p.Length).Trim();
            return t;
        }

        void OnObjective(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (_objectiveVisible) UiAnim.Out(_objective, 0.16f, new Vector2(-6f, 0f));
                _objectiveVisible = false;
                return;
            }
            if (!_objectiveVisible || !_visible)
            {
                _objectiveText.text = text;
                if (_visible) UiAnim.In(_objective, 0.22f, 0f, new Vector2(-8f, 0f));
                else UiAnim.Set(_objective, true);
                _objectiveVisible = true;
                return;
            }
            // Cambio di obiettivo: il vecchio esce, il nuovo entra (stati sovrapposti, niente salti).
            UiAnim.Out(_objective, 0.12f, new Vector2(0f, -4f), 1f, false, () =>
            {
                _objectiveText.text = text;
                UiAnim.In(_objective, 0.2f, 0f, new Vector2(0f, 6f));
                UiAnim.Pulse(_objective, 1.03f, 0.24f);
            });
        }

        // ------------------------------------------------------------------ visibilità

        public bool IsVisible => _visible;

        public void Show()
        {
            if (_visible) return;
            _visible = true;
            // Al ritorno nell'HUD i valori sono quelli veri, senza animazioni vecchie.
            _cashShown = _cashTarget = HudBus.Cash;
            SetCashText(_cashTarget);
            SetStars(HudBus.Wanted, false);
            Root.style.display = DisplayStyle.Flex;
            UiAnim.In(Root, 0.25f);
            if (HudBus.Area != null) { _areaName.text = HudBus.Area; ShowAreaBanner(); }
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            UiAnim.Out(Root, UiAnim.ExitTime(0.25f));
        }

        public void Tick(float dt)
        {
            if (!_visible) return;
            if (_areaHold > 0f)
            {
                _areaHold -= dt;
                if (_areaHold <= 0f) UiAnim.Out(_area, 0.2f, new Vector2(0f, -4f));
            }
            if (_deltaHold > 0f)
            {
                _deltaHold -= dt;
                if (_deltaHold <= 0f)
                {
                    UiAnim.Out(_cashDelta, 0.18f, new Vector2(0f, -4f));
                    _cashRow.RemoveFromClassList("uw-cash--gain");
                    _cashRow.RemoveFromClassList("uw-cash--loss");
                }
            }
        }
    }
}
