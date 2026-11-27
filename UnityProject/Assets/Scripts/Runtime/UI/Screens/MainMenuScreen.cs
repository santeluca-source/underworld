using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Underworld.Core;

namespace Underworld.UI
{
    /// <summary>
    /// Menu principale: titolo gigante, pannello di vetro con nome e pulsanti, stato della connessione.
    /// Le azioni chiamano GameServices.Launcher (la parte di rete). Se il launcher manca, mostra un messaggio chiaro.
    /// Un solo pannello di vetro: le viste (home, unisciti, stato, errore) si scambiano dentro lo stesso pannello.
    /// </summary>
    public sealed class MainMenuScreen
    {
        public const string PrefName = "underworld.playerName";
        public const string PrefJoinAddress = "underworld.joinAddress";
        public const string PrefJoinPort = "underworld.joinPort";

        enum View { Home, Join, Busy, Error, Unavailable }
        enum PendingAction { None, Play, Host, Join }

        public VisualElement Root { get; }

        readonly VisualElement _titleBlock;
        readonly VisualElement _panel;
        readonly VisualElement _footer;
        readonly Dictionary<View, VisualElement> _views = new Dictionary<View, VisualElement>();
        View _view = View.Home;
        bool _visible;

        // Home
        TextField _name;
        VisualElement _nameField;
        Label _nameHint;
        bool _nameTouched;

        // Unisciti
        TextField _joinAddress;
        TextField _joinPort;
        Label _joinError;

        // Stato
        Label _busyTitle;
        Label _busyText;
        UiIcon _busySpinner;
        VisualElement _steps;
        VisualElement _barTrack;
        VisualElement _barFill;
        float _barPhase;
        float _spin;

        // Errore
        Label _errorTitle;
        Label _errorText;
        Button _retry;

        // Azione da ripetere con "Riprova".
        PendingAction _last = PendingAction.None;
        string _lastIp;
        ushort _lastPort;
        bool _failureDismissed;
        LauncherState? _lastState;

        public MainMenuScreen()
        {
            Root = UiBuild.Layer("main-menu");
            Root.AddToClassList("uw-menu");
            Root.style.display = DisplayStyle.None;

            // Titolo gigante. Nessuna etichetta sopra, nessun punto finale.
            _titleBlock = UiBuild.Box("uw-menu__title-block", PickingMode.Ignore);
            _titleBlock.Add(UiBuild.Text("UNDERWORLD", "uw-menu__title"));
            _titleBlock.Add(UiBuild.Text("The city never sleeps", "uw-menu__tagline"));
            Root.Add(_titleBlock);

            _panel = UiBuild.Glass("uw-menu__panel");
            Root.Add(_panel);

            _views[View.Home] = BuildHome();
            _views[View.Join] = BuildJoin();
            _views[View.Busy] = BuildBusy();
            _views[View.Error] = BuildError();
            _views[View.Unavailable] = BuildUnavailable();
            foreach (var v in _views.Values)
            {
                v.style.display = DisplayStyle.None;
                _panel.Add(v);
            }
            _views[View.Home].style.display = DisplayStyle.Flex;

            _footer = UiBuild.Box("uw-menu__footer", PickingMode.Ignore);
            _footer.Add(UiBuild.Text("Prototype in development", UiClasses.Small, UiClasses.Muted));
            Root.Add(_footer);
        }

        // ------------------------------------------------------------------ costruzione

        VisualElement BuildHome()
        {
            var v = UiBuild.Box("uw-menu__view");
            string saved = PlayerPrefs.GetString(PrefName, string.Empty);
            if (string.IsNullOrEmpty(saved)) saved = LaunchArgs.Current.PlayerName ?? string.Empty;
            _name = UiBuild.Field("Your name", saved, "Enter your name", PlayerNameRules.MaxLength, out _nameField, out _nameHint);
            _name.RegisterValueChangedCallback(_ => { _nameTouched = true; RefreshNameHint(); });
            _name.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.StopPropagation(); OnPlay(); }
            }, TrickleDown.TrickleDown);
            v.Add(_nameField);
            RefreshNameHint();

            var actions = UiBuild.Box("uw-menu__actions");
            actions.Add(UiBuild.Button("Play", OnPlay, UiClasses.ButtonPrimary));
            actions.Add(TwoLineButton("Play with friends", "Friends join with your IP address", OnHost));
            actions.Add(TwoLineButton("Join", "Join a friend's game", () => ShowView(View.Join)));
            v.Add(actions);

            var secondary = UiBuild.Box("uw-menu__secondary");
            secondary.Add(UiBuild.Button("Credits", () => UiWindows.Open(CreditsWindow.Id), UiClasses.ButtonQuiet));
            secondary.Add(UiBuild.Button("Quit", OnQuit, UiClasses.ButtonQuiet));
            v.Add(secondary);
            return v;
        }

        static Button TwoLineButton(string title, string description, Action onClick)
        {
            var b = UiBuild.Button(string.Empty, onClick, UiClasses.ButtonGhost);
            b.AddToClassList("uw-btn--two-line");
            b.Add(UiBuild.Text(title, "uw-btn__title"));
            b.Add(UiBuild.Text(description, "uw-btn__desc"));
            return b;
        }

        VisualElement BuildJoin()
        {
            var v = UiBuild.Box("uw-menu__view");
            v.Add(ViewHeader("Join a game", () => ShowView(View.Home)));

            string addr = PlayerPrefs.GetString(PrefJoinAddress, "127.0.0.1");
            int port = PlayerPrefs.GetInt(PrefJoinPort, JoinAddress.DefaultPort);
            _joinAddress = UiBuild.Field("IP address", addr, "192.168.1.20", 64, out var addrField, out _);
            _joinPort = UiBuild.Field("Port", port.ToString(), "7777", 5, out var portField, out _);
            var row = UiBuild.Box("uw-menu__row");
            addrField.AddToClassList("uw-grow");
            portField.AddToClassList("uw-menu__port");
            row.Add(addrField);
            row.Add(portField);
            v.Add(row);

            _joinError = UiBuild.Text("Ask the friend who hosts the game for the address", UiClasses.FieldHint, "uw-menu__join-hint");
            v.Add(_joinError);

            // Invio nei campi = Collegati.
            EventCallback<KeyDownEvent> enter = e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.StopPropagation(); OnJoin(); }
            };
            _joinAddress.RegisterCallback(enter, TrickleDown.TrickleDown);
            _joinPort.RegisterCallback(enter, TrickleDown.TrickleDown);

            var actions = UiBuild.Box("uw-menu__actions");
            actions.Add(UiBuild.Button("Connect", OnJoin, UiClasses.ButtonPrimary));
            v.Add(actions);
            return v;
        }

        VisualElement BuildBusy()
        {
            var v = UiBuild.Box("uw-menu__view");
            var head = UiBuild.Box("uw-status__head");
            _busySpinner = new UiIcon(IconKind.Arc, 20f) { Stroke = 2.5f };
            _busySpinner.AddToClassList("uw-status__spinner");
            _busyTitle = UiBuild.Text("Starting the game", UiClasses.Heading);
            head.Add(_busySpinner);
            head.Add(_busyTitle);
            v.Add(head);

            _busyText = UiBuild.Text(string.Empty, UiClasses.Body, UiClasses.Muted, "uw-status__text");
            _busyText.enableRichText = false;
            v.Add(_busyText);

            _steps = UiBuild.Box("uw-steps", PickingMode.Ignore);
            v.Add(_steps);

            _barTrack = UiBuild.Box("uw-progress", PickingMode.Ignore);
            _barFill = UiBuild.Box("uw-progress__fill", PickingMode.Ignore);
            _barTrack.Add(_barFill);
            v.Add(_barTrack);

            var actions = UiBuild.Box("uw-menu__actions");
            actions.Add(UiBuild.Button("Cancel", OnCancel, UiClasses.ButtonGhost));
            v.Add(actions);
            return v;
        }

        VisualElement BuildError()
        {
            var v = UiBuild.Box("uw-menu__view");
            var head = UiBuild.Box("uw-status__head");
            var icon = new UiIcon(IconKind.Error, 20f);
            icon.AddToClassList("uw-icon--danger");
            _errorTitle = UiBuild.Text("Connection failed", UiClasses.Heading);
            head.Add(icon);
            head.Add(_errorTitle);
            v.Add(head);
            _errorText = UiBuild.Text(string.Empty, UiClasses.Body, "uw-status__text");
            _errorText.enableRichText = false; // il motivo può arrivare dal server: testo semplice
            v.Add(_errorText);

            var actions = UiBuild.Box("uw-menu__actions-row");
            _retry = UiBuild.Button("Retry", OnRetry, UiClasses.ButtonPrimary);
            _retry.AddToClassList("uw-grow");
            var back = UiBuild.Button("Back", DismissFailure, UiClasses.ButtonGhost);
            back.AddToClassList("uw-grow");
            actions.Add(_retry);
            actions.Add(back);
            v.Add(actions);
            return v;
        }

        VisualElement BuildUnavailable()
        {
            var v = UiBuild.Box("uw-menu__view");
            var head = UiBuild.Box("uw-status__head");
            var icon = new UiIcon(IconKind.Info, 20f);
            icon.AddToClassList("uw-icon--info");
            head.Add(icon);
            head.Add(UiBuild.Text("Game not available", UiClasses.Heading));
            v.Add(head);
            v.Add(UiBuild.Text("This version does not include online play yet. It comes with the next update.",
                UiClasses.Body, UiClasses.Muted, "uw-status__text"));
            var actions = UiBuild.Box("uw-menu__actions");
            actions.Add(UiBuild.Button("Back", () => ShowView(View.Home), UiClasses.ButtonGhost));
            v.Add(actions);
            return v;
        }

        static VisualElement ViewHeader(string title, Action back)
        {
            var head = UiBuild.Box("uw-view-header");
            var b = UiBuild.IconButton(IconKind.ChevronLeft, back, "Back");
            b.AddToClassList("uw-view-header__back");
            head.Add(b);
            head.Add(UiBuild.Text(title, UiClasses.Heading));
            return head;
        }

        // ------------------------------------------------------------------ visibilità

        public bool IsVisible => _visible;

        public void Show()
        {
            if (_visible) return;
            _visible = true;
            Root.style.display = DisplayStyle.Flex;
            UiAnim.In(Root, 0.2f);
            UiAnim.In(_titleBlock, 0.25f, 0.02f, new Vector2(0f, 10f));
            UiAnim.In(_panel, 0.25f, 0.06f, new Vector2(0f, 12f));
            UiAnim.StaggerChildren(CurrentView(), 0.1f);
            UiAnim.In(_footer, 0.25f, 0.2f);
            RefreshFromLauncher(true);
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            UiAnim.Out(Root, UiAnim.ExitTime(0.25f));
        }

        VisualElement CurrentView() => _views[_view];

        void ShowView(View next, bool animate = true)
        {
            if (next == _view && _views[next].style.display == DisplayStyle.Flex) return;
            var old = _views[_view];
            var nv = _views[next];
            _view = next;
            UiAnim.Set(old, false);
            nv.style.display = DisplayStyle.Flex;
            if (animate && _visible)
            {
                UiAnim.In(nv, UiAnim.FastEnterTime, 0f, new Vector2(0f, 6f));
                UiAnim.StaggerChildren(nv, 0.02f, 6f, UiAnim.FastEnterTime);
            }
            else UiAnim.Set(nv, true);

            if (next == View.Join) _joinAddress.schedule.Execute(() => _joinAddress.Focus()).StartingIn(30);
        }

        /// <summary>Esc nel menu: torna alla vista principale. Ritorna true se ha fatto qualcosa.</summary>
        public bool Back()
        {
            switch (_view)
            {
                case View.Join:
                case View.Unavailable:
                    ShowView(View.Home);
                    return true;
                case View.Error:
                    DismissFailure();
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ azioni

        static IGameLauncher Launcher => GameServices.Launcher;

        bool TryGetName(out string name)
        {
            name = PlayerNameRules.Normalize(_name.value);
            _nameTouched = true;
            RefreshNameHint();
            if (PlayerNameRules.IsValid(name)) return true;
            _name.Focus();
            UiAnim.Pulse(_nameHint, 1.04f, 0.22f);
            return false;
        }

        void SaveName(string name)
        {
            PlayerPrefs.SetString(PrefName, name);
            PlayerPrefs.Save();
        }

        void OnPlay() => StartAction(PendingAction.Play);
        void OnHost() => StartAction(PendingAction.Host);

        void StartAction(PendingAction action)
        {
            if (!TryGetName(out string name)) return;
            SaveName(name);
            if (Launcher == null) { ShowView(View.Unavailable); return; }
            _last = action;
            _failureDismissed = false;
            ShowBusy(action == PendingAction.Host ? "Starting the game with friends" : "Starting the game", "Getting ready…");
            if (action == PendingAction.Host) Launcher.HostForFriends(name);
            else Launcher.PlayLocal(name);
            RefreshFromLauncher(false);
        }

        void OnJoin()
        {
            if (!JoinAddress.TryParse(_joinAddress.value, _joinPort.value, out string ip, out ushort port, out string error))
            {
                _joinError.text = error;
                _joinError.AddToClassList("uw-field__hint--error");
                UiAnim.Pulse(_joinError, 1.03f, 0.22f);
                return;
            }
            _joinError.RemoveFromClassList("uw-field__hint--error");
            _joinError.text = "Ask the friend who hosts the game for the address";
            if (!TryGetName(out string name)) { ShowView(View.Home); return; }
            SaveName(name);
            PlayerPrefs.SetString(PrefJoinAddress, ip);
            PlayerPrefs.SetInt(PrefJoinPort, port);
            PlayerPrefs.Save();
            if (Launcher == null) { ShowView(View.Unavailable); return; }
            _last = PendingAction.Join;
            _lastIp = ip;
            _lastPort = port;
            _failureDismissed = false;
            ShowBusy("Connecting to " + ip, "Getting ready…");
            Launcher.Join(ip, port, name);
            RefreshFromLauncher(false);
        }

        void OnRetry()
        {
            switch (_last)
            {
                case PendingAction.Play: StartAction(PendingAction.Play); break;
                case PendingAction.Host: StartAction(PendingAction.Host); break;
                case PendingAction.Join:
                    _joinAddress.value = _lastIp ?? _joinAddress.value;
                    _joinPort.value = _lastPort > 0 ? _lastPort.ToString() : _joinPort.value;
                    OnJoin();
                    break;
                default: DismissFailure(); break;
            }
        }

        void OnCancel()
        {
            // L'interfaccia del launcher non ha "annulla": Disconnect esce e torna al menu.
            Launcher?.Disconnect();
            _last = PendingAction.None;
            ShowView(View.Home);
        }

        void DismissFailure()
        {
            _failureDismissed = true;
            ShowView(View.Home);
        }

        static void OnQuit()
        {
            if (Launcher != null) Launcher.QuitGame();
            else Application.Quit();
        }

        void RefreshNameHint()
        {
            var check = PlayerNameRules.Check(_name.value);
            bool showError = _nameTouched && check != NameCheck.Ok;
            _nameHint.text = check == NameCheck.Ok || !_nameTouched
                ? (check == NameCheck.Ok ? PlayerNameRules.Message(NameCheck.Ok) : "3 to 16 characters: letters, numbers, space, _ and -")
                : PlayerNameRules.Message(check);
            _nameField.EnableInClassList(UiClasses.FieldInvalid, showError);
            _nameField.EnableInClassList(UiClasses.FieldValid, check == NameCheck.Ok);
        }

        // ------------------------------------------------------------------ stato del launcher

        /// <summary>Chiamato da UiRoot quando cambia lo stato del launcher (o il launcher stesso).</summary>
        public void OnLauncherChanged() => RefreshFromLauncher(false);

        void RefreshFromLauncher(bool entering)
        {
            var l = Launcher;
            LauncherState? state = l != null ? l.State : (LauncherState?)null;
            bool changed = state != _lastState;
            _lastState = state;
            if (l == null)
            {
                if (_view == View.Busy || _view == View.Error) ShowView(View.Home, !entering);
                return;
            }

            switch (l.State)
            {
                case LauncherState.StartingServer:
                case LauncherState.Connecting:
                case LauncherState.Transferring:
                    if (_view != View.Busy) ShowBusy(DefaultBusyTitle(), l.StatusText);
                    _busyText.text = string.IsNullOrEmpty(l.StatusText) ? "Please wait…" : l.StatusText;
                    UpdateSteps(l.State);
                    break;
                case LauncherState.Failed:
                    if (changed) _failureDismissed = false;
                    if (!_failureDismissed)
                    {
                        _errorTitle.text = l.DisconnectedInGame ? "Disconnected" : "Connection failed";
                        _errorText.text = string.IsNullOrEmpty(l.StatusText) ? "Something went wrong. Try again in a moment." : l.StatusText;
                        _retry.style.display = _last == PendingAction.None ? DisplayStyle.None : DisplayStyle.Flex;
                        if (_view != View.Error) ShowView(View.Error, !entering);
                    }
                    break;
                case LauncherState.Idle:
                    if (_view == View.Busy || _view == View.Error) ShowView(View.Home, !entering);
                    break;
                case LauncherState.InGame:
                    // Il modo passa all'HUD (lo decide UiRoot). Al ritorno al menu si riparte dalla home.
                    _view = View.Home;
                    foreach (var kv in _views) kv.Value.style.display = kv.Key == View.Home ? DisplayStyle.Flex : DisplayStyle.None;
                    break;
            }
        }

        string DefaultBusyTitle() => _last == PendingAction.Join ? "Connecting" : _last == PendingAction.Host ? "Starting the game with friends" : "Starting the game";

        void ShowBusy(string title, string text)
        {
            _busyTitle.text = title;
            _busyText.text = string.IsNullOrEmpty(text) ? "Please wait…" : text;
            BuildSteps();
            UpdateSteps(Launcher != null ? Launcher.State : LauncherState.StartingServer);
            if (_view != View.Busy) ShowView(View.Busy);
        }

        void BuildSteps()
        {
            _steps.Clear();
            if (_last == PendingAction.Join) { _steps.style.display = DisplayStyle.None; return; }
            _steps.style.display = DisplayStyle.Flex;
            AddStep("Server");
            AddStep("Connect");
            AddStep("In the city");
        }

        void AddStep(string label)
        {
            var s = UiBuild.Box("uw-step", PickingMode.Ignore);
            var dot = new UiIcon(IconKind.Dot, 8f);
            dot.AddToClassList("uw-step__dot");
            s.Add(dot);
            s.Add(UiBuild.Text(label, "uw-step__label"));
            _steps.Add(s);
        }

        void UpdateSteps(LauncherState state)
        {
            if (_steps.childCount == 0) return;
            int current = state == LauncherState.StartingServer ? 0
                : state == LauncherState.Connecting ? 1
                : state == LauncherState.Transferring ? 2
                : state == LauncherState.InGame ? 3 : 0;
            for (int i = 0; i < _steps.childCount; i++)
            {
                var s = _steps[i];
                s.EnableInClassList("uw-step--done", i < current);
                s.EnableInClassList("uw-step--current", i == current);
                var dot = s.Q<UiIcon>();
                if (dot != null) { dot.Kind = i < current ? IconKind.Check : IconKind.Dot; dot.Filled = i <= current; }
            }
        }

        /// <summary>Animazioni continue (spinner e barra indeterminata). Solo quando la vista è visibile.</summary>
        public void Tick(float dt)
        {
            if (!_visible || _view != View.Busy) return;
            _spin = (_spin + dt * 360f) % 360f;
            _busySpinner.style.rotate = new Rotate(_spin);
            // Barra: un tratto corto che corre sulla traccia (ease-in-out, 1,4 s per giro).
            _barPhase = (_barPhase + dt / 1.4f) % 1f;
            float w = _barTrack.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0f) return;
            float seg = w * 0.32f;
            float k = Easing.Apply(Ease.InOutCubic, _barPhase);
            _barFill.style.width = seg;
            _barFill.style.left = Mathf.Lerp(-seg, w, k);
        }

        // ------------------------------------------------------------------ anteprima (solo -uipreview)

        internal void PreviewSetName(string name)
        {
            _name.SetValueWithoutNotify(name);
            _nameTouched = false;
            RefreshNameHint();
        }

        internal void PreviewShowJoin() => ShowView(View.Join, false);
        internal void PreviewShowUnavailable() => ShowView(View.Unavailable, false);
        internal void PreviewSetLast(bool join) => _last = join ? PendingAction.Join : PendingAction.Play;
    }
}
