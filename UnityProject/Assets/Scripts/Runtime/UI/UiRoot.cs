using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Underworld.Core;

namespace Underworld.UI
{
    /// <summary>
    /// Radice della UI del client. Nasce da sola all'avvio (mai sul server o in batchmode senza grafica)
    /// e resta tra le scene. Contiene: UIDocument + PanelSettings (scala con lo schermo, 1920x1080, match 0,5),
    /// le schermate (menu, HUD, pausa, caricamento), le finestre e gli avvisi.
    ///
    /// La schermata dipende dalla scena attiva e da GameServices.Launcher.State (vedi <see cref="UiModeRules"/>).
    /// Mentre una schermata o finestra è aperta: InputGate bloccato e mouse libero.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class UiRoot : MonoBehaviour
    {
        /// <summary>Percorso in Resources del PanelSettings (creato dal passo di setup UiAssetsStep).</summary>
        public const string PanelSettingsResource = "Underworld/UiPanelSettings";

        public static UiRoot Instance { get; private set; }

        public UiTweener Tweener { get; } = new UiTweener();
        public UiScreenMachine Machine { get; } = new UiScreenMachine();
        public UiWindowStack Windows { get; private set; }
        public VisualElement RootElement { get; private set; }

        /// <summary>True se un menu, la pausa o una finestra ha il mouse (cursore libero).</summary>
        public bool WantsCursor { get; private set; }

        UIDocument _document;
        MainMenuScreen _menu;
        HudScreen _hud;
        PauseScreen _pause;
        LoadingOverlay _loading;
        ToastStack _toasts;
        VisualElement _overlayToastHost;
        bool _built;
        bool _contextDirty = true;
        bool _blocking;
        IGameLauncher _launcher;

        // ------------------------------------------------------------------ avvio

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (!ShouldCreate()) return;
            var ps = Resources.Load<PanelSettings>(PanelSettingsResource);
            if (ps == null)
            {
                Debug.LogError($"[UI] PanelSettings mancante in Resources/{PanelSettingsResource}. Esegui il setup (Tools/unity.ps1 -Action setup).");
                return;
            }
            var go = new GameObject("UiRoot");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            var root = go.AddComponent<UiRoot>();
            root._document = doc;
            go.SetActive(true);
        }

        /// <summary>La UI non nasce sul server né in batchmode senza grafica (bot dei test).</summary>
        public static bool ShouldCreate()
        {
            if (LaunchArgs.Current.IsServer) return false;
            if (Application.isBatchMode && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
            return true;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (_document == null) _document = GetComponent<UIDocument>();
        }

        void Start()
        {
            Build();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            HudBus.ToastRequested += OnToast;
            UiRegistry.SheetAdded += OnSheetAdded;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            HudBus.ToastRequested -= OnToast;
            UiRegistry.SheetAdded -= OnSheetAdded;
            SetLauncher(null);
            _hud?.Dispose();
            Windows?.CloseAll();
            if (_blocking) InputGate.Unblock(this);
            Instance = null;
        }

        void Build()
        {
            if (_built || _document == null) return;
            RootElement = _document.rootVisualElement;
            if (RootElement == null)
            {
                Debug.LogError("[UI] UIDocument senza rootVisualElement");
                return;
            }
            _built = true;
            RootElement.pickingMode = PickingMode.Ignore;
            RootElement.AddToClassList(UiClasses.Root);
            foreach (var sheet in UiRegistry.StyleSheets) RootElement.styleSheets.Add(sheet);

            var screens = UiBuild.Layer("screens");
            var pauseLayer = UiBuild.Layer("pause-layer");
            var loadingLayer = UiBuild.Layer("loading-layer");
            var windowLayer = UiBuild.Layer("window-layer");
            var overlayLayer = UiBuild.Layer("overlay-layer");
            RootElement.Add(screens);
            RootElement.Add(pauseLayer);
            RootElement.Add(loadingLayer);
            RootElement.Add(windowLayer);
            RootElement.Add(overlayLayer);

            _menu = new MainMenuScreen();
            _hud = new HudScreen();
            screens.Add(_menu.Root);
            screens.Add(_hud.Root);

            _pause = new PauseScreen(() => Machine.SetPause(false));
            pauseLayer.Add(_pause.Root);

            _loading = new LoadingOverlay();
            loadingLayer.Add(_loading.Root);

            Windows = new UiWindowStack(windowLayer);
            Windows.Changed += UpdateCursorAndBlock;

            _toasts = new ToastStack();
            _overlayToastHost = UiBuild.Box("uw-overlay-toasts", PickingMode.Ignore);
            overlayLayer.Add(_overlayToastHost);
            _overlayToastHost.Add(_toasts.Root);

            if (!UiRegistry.HasWindow(CreditsWindow.Id)) UiRegistry.RegisterWindow(CreditsWindow.Id, () => new CreditsWindow());

            Machine.ModeChanged += OnModeChanged;
            Machine.PauseChanged += OnPauseChanged;
            _contextDirty = true;
        }

        // ------------------------------------------------------------------ contesto (scena + launcher)

        void OnActiveSceneChanged(Scene oldScene, Scene newScene) => _contextDirty = true;

        void SetLauncher(IGameLauncher launcher)
        {
            if (ReferenceEquals(launcher, _launcher)) return;
            if (_launcher != null) _launcher.StateChanged -= OnLauncherStateChanged;
            _launcher = launcher;
            if (_launcher != null) _launcher.StateChanged += OnLauncherStateChanged;
            _contextDirty = true;
        }

        // Può arrivare da qualsiasi punto del codice di rete: qui segno solo che va riletto in Update.
        void OnLauncherStateChanged() => _contextDirty = true;

        void RefreshContext()
        {
            _contextDirty = false;
            string scene = SceneManager.GetActiveScene().name;
            LauncherState? state = _launcher != null ? _launcher.State : (LauncherState?)null;
            Machine.SetContext(scene, state);
            _menu.OnLauncherChanged();
            if (_loading.IsVisible) _loading.Refresh();
        }

        void OnModeChanged(UiMode oldMode, UiMode newMode)
        {
            Windows.CloseAll();
            switch (newMode)
            {
                case UiMode.Menu:
                    _hud.Hide(); _loading.Hide(); _menu.Show();
                    break;
                case UiMode.Hud:
                    _menu.Hide(); _loading.Hide(); _hud.Show();
                    break;
                case UiMode.Loading:
                    _menu.Hide(); _hud.Hide(); _loading.Show();
                    break;
                default:
                    _menu.Hide(); _hud.Hide(); _loading.Hide();
                    break;
            }
            // Gli avvisi stanno sotto soldi e stelle nell'HUD, in alto a destra altrove.
            var host = newMode == UiMode.Hud ? _hud.ToastHost : _overlayToastHost;
            if (_toasts.Root.parent != host) host.Add(_toasts.Root);
            UpdateCursorAndBlock();
        }

        void OnPauseChanged(bool open)
        {
            if (open) _pause.Show(); else _pause.Hide();
            UpdateCursorAndBlock();
        }

        void UpdateCursorAndBlock()
        {
            bool block = Machine.BlocksGameplay;
            if (block != _blocking)
            {
                _blocking = block;
                if (block) InputGate.Block(this); else InputGate.Unblock(this);
            }
            bool windowWantsMouse = false;
            if (Windows != null)
            {
                var top = Windows.Top;
                windowWantsMouse = top != null && top.BlocksGameplay;
            }
            WantsCursor = block || windowWantsMouse;
        }

        // ------------------------------------------------------------------ ciclo

        void Update()
        {
            if (!_built) return;
            SetLauncher(GameServices.Launcher);
            if (_contextDirty) RefreshContext();

            HandleEscape();

            float dt = Time.unscaledDeltaTime;
            Tweener.Update(dt);
            _menu.Tick(dt);
            _hud.Tick(dt);
            _loading.Tick(dt);
            _toasts.Tick(dt);
        }

        void LateUpdate()
        {
            // Con un menu o una finestra aperti il mouse deve essere libero (anche se un altro script lo blocca).
            if (!WantsCursor) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        void HandleEscape()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool esc = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame);
            if (!esc) return;
            switch (Machine.Escape(Windows.Count))
            {
                case EscapeAction.CloseWindow: Windows.CloseTop(); break;
                case EscapeAction.OpenPause: Machine.SetPause(true); break;
                case EscapeAction.ClosePause: Machine.SetPause(false); break;
                case EscapeAction.MenuBack: _menu.Back(); break;
            }
        }

        void OnToast(string text, ToastKind kind) => _toasts?.Push(text, kind);

        void OnSheetAdded(StyleSheet sheet)
        {
            if (RootElement != null && !RootElement.styleSheets.Contains(sheet)) RootElement.styleSheets.Add(sheet);
        }

        /// <summary>Senza launcher (rami senza rete): "Torna al menu" carica direttamente la scena del menu.</summary>
        public void FallbackToMenu()
        {
            if (Application.CanStreamedLevelBeLoaded(UiModeRules.MenuScene)) SceneManager.LoadScene(UiModeRules.MenuScene);
            else Debug.LogWarning("[UI] La scena MainMenu non è nella build");
        }

        // ------------------------------------------------------------------ per l'anteprima (-uipreview)

        internal MainMenuScreen Menu => _menu;
        internal HudScreen Hud => _hud;
        internal UIDocument Document => _document;

        /// <summary>Forza la rilettura di scena e launcher al prossimo frame.</summary>
        public void MarkContextDirty() => _contextDirty = true;
    }
}
