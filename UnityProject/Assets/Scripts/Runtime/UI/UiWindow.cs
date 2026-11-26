using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Underworld.UI
{
    /// <summary>
    /// Finestra riutilizzabile: pannello di vetro con titolo, pulsante chiudi, area del contenuto e piè di pagina.
    /// Aperta → blocca il gioco (InputGate) e libera il mouse; Esc la chiude; entra da 0,95 con ease-out.
    ///
    /// Uso da un modulo (senza toccare i file della UI):
    /// <code>
    /// var w = new UiWindow("Garage");
    /// w.Content.Add(new Label("Nessun veicolo"));
    /// w.AddFooterButton("Chiudi", w.Close);
    /// w.Open();
    /// </code>
    /// Oppure si registra una fabbrica: <c>UiRegistry.RegisterWindow("garage", () => new GarageWindow())</c>
    /// e si apre con <c>UiWindows.Open("garage")</c>.
    /// </summary>
    public class UiWindow : VisualElement
    {
        public const string UssClass = "uw-window";

        readonly Label _title;
        readonly VisualElement _panel;
        readonly VisualElement _footer;
        readonly ScrollView _scroll;

        /// <summary>Id usato dal registro (può essere null per le finestre create al volo).</summary>
        public string WindowId { get; internal set; }

        /// <summary>Pannello di vetro (per aggiungere classi o cambiare la larghezza).</summary>
        public VisualElement Panel => _panel;

        /// <summary>Area del contenuto (scorre se è troppo alta).</summary>
        public VisualElement Content => _scroll.contentContainer;

        /// <summary>Riga dei pulsanti in basso (nascosta se vuota).</summary>
        public VisualElement Footer => _footer;

        public string Title
        {
            get => _title.text;
            set => _title.text = value;
        }

        public bool IsOpen { get; internal set; }

        /// <summary>Esc chiude la finestra (default true).</summary>
        public bool CloseOnEscape { get; set; } = true;

        /// <summary>Blocca il personaggio mentre è aperta (default true).</summary>
        public bool BlocksGameplay { get; set; } = true;

        /// <summary>Scurisce lo sfondo dietro la finestra (default true).</summary>
        public bool ShowScrim { get; set; } = true;

        public event Action Opened;
        public event Action Closed;

        public UiWindow(string title, float width = 560f)
        {
            AddToClassList(UssClass);
            pickingMode = PickingMode.Ignore;

            _panel = UiBuild.Glass("uw-window__panel");
            _panel.style.width = width;

            var header = UiBuild.Box("uw-window__header");
            _title = UiBuild.Text(title, "uw-window__title");
            var close = UiBuild.IconButton(IconKind.Close, Close, "Chiudi");
            close.AddToClassList("uw-window__close");
            header.Add(_title);
            header.Add(close);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("uw-window__scroll");
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _scroll.contentContainer.AddToClassList("uw-window__content");

            _footer = UiBuild.Box("uw-window__footer");
            _footer.style.display = DisplayStyle.None;

            _panel.Add(header);
            _panel.Add(_scroll);
            _panel.Add(_footer);
            Add(_panel);

            // Un clic sul pannello non deve arrivare allo sfondo.
            _panel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
        }

        /// <summary>Aggiunge un pulsante al piè di pagina. Il primo con primary=true è quello principale.</summary>
        public Button AddFooterButton(string text, Action onClick, bool primary = false)
        {
            var b = UiBuild.Button(text, onClick, primary ? UiClasses.ButtonPrimary : UiClasses.ButtonGhost);
            _footer.Add(b);
            _footer.style.display = DisplayStyle.Flex;
            return b;
        }

        public void Open() => UiWindows.Open(this);
        public void Close() => UiWindows.Close(this);

        /// <summary>Chiamato a finestra aperta (dopo l'aggiunta alla UI).</summary>
        protected internal virtual void OnOpened() { }

        /// <summary>Chiamato a finestra chiusa (prima della rimozione dalla UI).</summary>
        protected internal virtual void OnClosed() { }

        internal void RaiseOpened()
        {
            OnOpened();
            Opened?.Invoke();
        }

        internal void RaiseClosed()
        {
            OnClosed();
            Closed?.Invoke();
        }

        internal void PlayOpen()
        {
            UiAnim.In(_panel, UiAnim.EnterTime, 0f, new Vector2(0f, 6f), UiAnim.PopoverScale);
            UiAnim.StaggerChildren(Content, 0.06f, 6f, UiAnim.FastEnterTime);
        }

        internal void PlayClose(Action done)
        {
            UiAnim.Out(_panel, UiAnim.ExitTime(UiAnim.EnterTime), new Vector2(0f, 4f), 0.97f, true, done);
        }
    }
}
