using UnityEngine;
using UnityEngine.UIElements;
using Underworld.Core;

namespace Underworld.UI
{
    /// <summary>
    /// Attesa a schermo pieno fuori dal menu: connessione diretta (-connect), trasferimento tra aree, errore.
    /// Testo dello stato dal launcher (StatusText) e un'uscita sempre disponibile ("Main menu").
    /// </summary>
    public sealed class LoadingOverlay
    {
        public VisualElement Root { get; }

        readonly VisualElement _center;
        readonly UiIcon _spinner;
        readonly UiIcon _errorIcon;
        readonly Label _title;
        readonly Label _text;
        readonly Button _back;
        float _spin;
        bool _visible;

        public LoadingOverlay()
        {
            Root = UiBuild.Layer("loading");
            Root.AddToClassList("uw-loading");
            Root.pickingMode = PickingMode.Position; // blocca i clic sul mondo
            Root.style.display = DisplayStyle.None;

            _center = UiBuild.Box("uw-loading__center");
            var head = UiBuild.Box("uw-status__head", PickingMode.Ignore);
            _spinner = new UiIcon(IconKind.Arc, 22f) { Stroke = 2.5f };
            _spinner.AddToClassList("uw-status__spinner");
            _errorIcon = new UiIcon(IconKind.Error, 22f);
            _errorIcon.AddToClassList("uw-icon--danger");
            _title = UiBuild.Text("Loading", UiClasses.Heading);
            head.Add(_spinner);
            head.Add(_errorIcon);
            head.Add(_title);
            _center.Add(head);
            _text = UiBuild.Text(string.Empty, UiClasses.Body, UiClasses.Muted, "uw-loading__text");
            _center.Add(_text);
            _back = UiBuild.Button("Main menu", OnBack, UiClasses.ButtonGhost);
            _back.AddToClassList("uw-loading__back");
            _center.Add(_back);
            Root.Add(_center);
        }

        public bool IsVisible => _visible;

        public void Show()
        {
            if (_visible) return;
            _visible = true;
            Root.style.display = DisplayStyle.Flex;
            UiAnim.In(Root, 0.2f);
            UiAnim.In(_center, 0.22f, 0.04f, new Vector2(0f, 8f));
            Refresh();
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            UiAnim.Out(Root, UiAnim.ExitTime(0.22f));
        }

        /// <summary>Aggiorna titolo e testo dallo stato del launcher.</summary>
        public void Refresh()
        {
            var l = GameServices.Launcher;
            LauncherState state = l != null ? l.State : LauncherState.Idle;
            bool failed = state == LauncherState.Failed;
            _spinner.style.display = failed ? DisplayStyle.None : DisplayStyle.Flex;
            _errorIcon.style.display = failed ? DisplayStyle.Flex : DisplayStyle.None;
            _title.text = state switch
            {
                LauncherState.StartingServer => "Starting the server",
                LauncherState.Connecting => "Connecting",
                LauncherState.Transferring => "Changing area",
                LauncherState.Failed => "Connection lost",
                _ => "Loading",
            };
            string status = l != null ? l.StatusText : null;
            _text.text = string.IsNullOrEmpty(status) ? (failed ? "The server is not responding" : "Please wait a few seconds") : status;
        }

        public void Tick(float dt)
        {
            if (!_visible) return;
            _spin = (_spin + dt * 360f) % 360f;
            _spinner.style.rotate = new Rotate(_spin);
        }

        static void OnBack()
        {
            var l = GameServices.Launcher;
            if (l != null) l.Disconnect();
            else UiRoot.Instance?.FallbackToMenu();
        }
    }
}
