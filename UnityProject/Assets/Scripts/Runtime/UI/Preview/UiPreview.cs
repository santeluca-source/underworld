using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Underworld.Core;

namespace Underworld.UI
{
    /// <summary>
    /// Anteprima della UI senza rete, per lo sviluppo e per gli screenshot automatici.
    ///
    ///   UNDERWORLD.exe -uipreview hud -autoshot C:\tmp\hud.png -screen-width 1920 -screen-height 1080 -popupwindow
    ///
    /// Modi: menu, menu-join, menu-busy, menu-error, menu-offline, credits, hud, pause, window.
    /// Usa un launcher finto (<see cref="PreviewLauncher"/>) e dati finti su HudBus.
    /// Con -autoshot salva lo screenshot dopo -autoshot-delay secondi (default 3) e chiude il gioco.
    /// Senza -uipreview non fa niente.
    /// </summary>
    public sealed class UiPreview : MonoBehaviour
    {
        public const string ArgMode = "uipreview";
        public const string ArgShot = "autoshot";
        public const string ArgShotDelay = "autoshot-delay";

        public static bool IsActive { get; private set; }

        string _mode;
        PreviewLauncher _launcher;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => IsActive = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var args = LaunchArgs.Current;
            if (!args.Has(ArgMode) || !UiRoot.ShouldCreate()) return;
            IsActive = true;
            var go = new GameObject("UiPreview");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<UiPreview>();
            p._mode = (args.Get(ArgMode, "menu") ?? "menu").ToLowerInvariant();
        }

        void Awake()
        {
            // Il launcher finto sostituisce quello vero solo in anteprima.
            _launcher = new PreviewLauncher();
            GameServices.Launcher = _mode == "menu-offline" ? null : _launcher;
            Debug.Log("[UiPreview] modo " + _mode);
        }

        IEnumerator Start()
        {
            if (SceneManager.GetActiveScene().name != UiModeRules.MenuScene)
            {
                if (Application.CanStreamedLevelBeLoaded(UiModeRules.MenuScene))
                {
                    SceneManager.LoadScene(UiModeRules.MenuScene);
                    yield return null;
                }
                else Debug.LogWarning("[UiPreview] La scena MainMenu non è nella build");
            }
            yield return null;

            var root = UiRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[UiPreview] UiRoot assente: niente da mostrare");
                yield break;
            }

            switch (_mode)
            {
                case "menu": yield return MenuHome(root); break;
                case "menu-join": yield return MenuJoin(root); break;
                case "menu-busy": yield return MenuBusy(root); break;
                case "menu-error": yield return MenuError(root); break;
                case "menu-offline": yield return MenuOffline(root); break;
                case "credits": yield return Credits(root); break;
                case "hud": yield return Hud(root, false, false); break;
                case "pause": yield return Hud(root, true, false); break;
                case "window": yield return Hud(root, false, true); break;
                default:
                    Debug.LogWarning("[UiPreview] Modo sconosciuto: " + _mode + ". Uso 'menu'.");
                    yield return MenuHome(root);
                    break;
            }
        }

        void Update()
        {
            // Un altro modulo potrebbe impostare il suo launcher: in anteprima vince quello finto.
            if (_mode != "menu-offline" && !ReferenceEquals(GameServices.Launcher, _launcher)) GameServices.Launcher = _launcher;
            _launcher.Tick(Time.unscaledDeltaTime);
        }

        // ------------------------------------------------------------------ scenari

        IEnumerator MenuHome(UiRoot root)
        {
            root.Menu?.PreviewSetName("Nico");
            yield return Shot(3f);
        }

        IEnumerator MenuJoin(UiRoot root)
        {
            root.Menu?.PreviewSetName("Nico");
            yield return new WaitForSecondsRealtime(0.4f);
            root.Menu?.PreviewShowJoin();
            yield return Shot(2.6f);
        }

        IEnumerator MenuBusy(UiRoot root)
        {
            root.Menu?.PreviewSetName("Nico");
            root.Menu?.PreviewSetLast(false);
            yield return new WaitForSecondsRealtime(0.4f);
            _launcher.Set(LauncherState.StartingServer, "Starting the Downtown server…");
            yield return new WaitForSecondsRealtime(0.9f);
            _launcher.Set(LauncherState.Connecting, "Connecting to Downtown…");
            yield return Shot(1.7f);
        }

        IEnumerator MenuError(UiRoot root)
        {
            root.Menu?.PreviewSetName("Nico");
            root.Menu?.PreviewSetLast(true);
            yield return new WaitForSecondsRealtime(0.4f);
            _launcher.Set(LauncherState.Failed, "The server is not responding. Check the address and that port 7777 is open.");
            yield return Shot(2.6f);
        }

        IEnumerator MenuOffline(UiRoot root)
        {
            root.Menu?.PreviewSetName("Nico");
            yield return new WaitForSecondsRealtime(0.4f);
            root.Menu?.PreviewShowUnavailable();
            yield return Shot(2.6f);
        }

        IEnumerator Credits(UiRoot root)
        {
            root.Menu?.PreviewSetName("Nico");
            yield return new WaitForSecondsRealtime(0.6f);
            UiWindows.Open(CreditsWindow.Id);
            yield return Shot(2.4f);
        }

        IEnumerator Hud(UiRoot root, bool pause, bool window)
        {
            // Vista da strada, come in gioco.
            if (MenuBackdrop.Instance != null) MenuBackdrop.Instance.SetPreset(MenuBackdrop.Preset.StreetDay);
            HudBus.SetCash(12500);
            _launcher.Set(LauncherState.InGame, "In game");
            yield return new WaitForSecondsRealtime(0.4f);

            HudBus.SetObjective("Drive the car to the Harbor Lane garage");
            yield return new WaitForSecondsRealtime(0.2f);
            HudBus.SetPrompt("Open the garage");
            HudBus.Toast("Game saved", ToastKind.Info);
            yield return new WaitForSecondsRealtime(0.3f);
            HudBus.SetWanted(2);
            HudBus.Toast("The police are looking for you", ToastKind.Warning);
            yield return new WaitForSecondsRealtime(0.3f);
            HudBus.SetArea(AreaInfo.DisplayName(AreaId.Downtown));
            yield return new WaitForSecondsRealtime(0.4f);
            HudBus.SetCash(14750);
            HudBus.Toast("Delivery complete: +$2,250", ToastKind.Success);

            if (pause)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                root.Machine.SetPause(true);
            }
            else if (window)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                // Come farebbe un modulo: finestra e elemento dell'HUD registrati, senza toccare i file della UI.
                if (!UiRegistry.HasWindow(SampleWindow.Id)) UiRegistry.RegisterWindow(SampleWindow.Id, () => new SampleWindow());
                UiRegistry.RegisterHudWidget("preview.speed", HudSlot.BottomRight,
                    () => UiBuild.Text("42 km/h", "uw-prompt__text", UiClasses.Tabular, UiClasses.Shadowed), 10);
                UiWindows.Open(SampleWindow.Id);
            }
            yield return Shot(pause || window ? 1.0f : 1.3f);
        }

        // ------------------------------------------------------------------ screenshot

        /// <summary>Se c'è -autoshot, aspetta e salva lo screenshot, poi chiude. <paramref name="settle"/> = attesa minima.</summary>
        IEnumerator Shot(float settle)
        {
            var args = LaunchArgs.Current;
            string path = args.AutoShot;
            if (string.IsNullOrEmpty(path)) yield break;
            float delay = settle;
            string d = args.Get(ArgShotDelay);
            if (d != null && float.TryParse(d, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) delay = Mathf.Max(0f, v);
            yield return new WaitForSecondsRealtime(delay);
            yield return new WaitForEndOfFrame();
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception e) { Debug.LogWarning("[UiPreview] Cartella dello screenshot: " + e.Message); }
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[UiPreview] screenshot {Screen.width}x{Screen.height} -> {path}");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        // ------------------------------------------------------------------ finta finestra di un modulo

        /// <summary>
        /// Esempio di finestra di un modulo (solo anteprima): mostra come un modulo usa UiWindow e il registro.
        /// </summary>
        sealed class SampleWindow : UiWindow
        {
            public const string Id = "preview.garage";

            public SampleWindow() : base("Garage", 520f)
            {
                AddRow("Gray sedan", "Ready", false);
                AddRow("Delivery van", "Ready", false);
                AddRow("Red coupe", "In repair · 2 min", true);
                AddFooterButton("Close", Close);
                AddFooterButton("Take the car", Close, true);
            }

            protected internal override void OnOpened() => Debug.Log("[UiPreview] finestra aperta: " + Title);

            void AddRow(string name, string state, bool muted)
            {
                var row = UiBuild.Box("uw-credits__row", PickingMode.Ignore);
                row.Add(UiBuild.Text(name, "uw-credits__who"));
                var s = UiBuild.Text(state, UiClasses.Small, muted ? UiClasses.Muted : null);
                row.Add(s);
                Content.Add(row);
            }
        }
    }

    /// <summary>
    /// Launcher finto per l'anteprima: nessuna rete. "Gioca" simula avvio, connessione e ingresso in gioco.
    /// </summary>
    public sealed class PreviewLauncher : IGameLauncher
    {
        public LauncherState State { get; private set; } = LauncherState.Idle;
        public string StatusText { get; private set; } = string.Empty;
        public event Action StateChanged;

        float _timer;
        bool _simulating;

        public void Set(LauncherState state, string text)
        {
            State = state;
            StatusText = text ?? string.Empty;
            StateChanged?.Invoke();
        }

        public void PlayLocal(string playerName) => Simulate();
        public void HostForFriends(string playerName) => Simulate();
        public void Join(string address, ushort port, string playerName) => Simulate();

        void Simulate()
        {
            _simulating = true;
            _timer = 0f;
            Set(LauncherState.StartingServer, "Starting the Downtown server…");
        }

        /// <summary>Fa avanzare la simulazione (chiamato da UiPreview).</summary>
        public void Tick(float dt)
        {
            if (!_simulating) return;
            _timer += dt;
            if (State == LauncherState.StartingServer && _timer > 1.5f) Set(LauncherState.Connecting, "Connecting to Downtown…");
            else if (State == LauncherState.Connecting && _timer > 3f)
            {
                _simulating = false;
                HudBus.SetCash(5000);
                HudBus.SetArea(AreaInfo.DisplayName(AreaId.Downtown));
                if (MenuBackdrop.Instance != null) MenuBackdrop.Instance.SetPreset(MenuBackdrop.Preset.StreetDay);
                Set(LauncherState.InGame, "In game");
            }
        }

        public void Disconnect()
        {
            _simulating = false;
            if (MenuBackdrop.Instance != null) MenuBackdrop.Instance.SetPreset(MenuBackdrop.Preset.MenuNight);
            Set(LauncherState.Idle, string.Empty);
        }

        public void QuitGame() => Application.Quit();
    }
}
