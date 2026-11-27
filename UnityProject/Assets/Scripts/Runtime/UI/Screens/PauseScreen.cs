using System;
using UnityEngine;
using UnityEngine.UIElements;
using Underworld.Core;

namespace Underworld.UI
{
    /// <summary>
    /// Menu di pausa (Esc in gioco). Il mondo NON si ferma (gioco in rete): la pausa blocca solo i comandi
    /// del personaggio (InputGate) e libera il mouse.
    /// </summary>
    public sealed class PauseScreen
    {
        public VisualElement Root { get; }

        readonly VisualElement _scrim;
        readonly VisualElement _panel;
        readonly VisualElement _list;
        readonly Label _context;
        readonly Action _resume;
        bool _visible;

        public PauseScreen(Action resume)
        {
            _resume = resume;
            Root = UiBuild.Layer("pause");
            Root.AddToClassList("uw-pause");
            Root.style.display = DisplayStyle.None;

            _scrim = UiBuild.Box("uw-scrim");
            _scrim.RegisterCallback<PointerDownEvent>(_ => _resume?.Invoke());
            Root.Add(_scrim);

            _panel = UiBuild.Glass("uw-pause__panel");
            _panel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            var head = UiBuild.Box("uw-pause__head", PickingMode.Ignore);
            head.Add(UiBuild.Text("Paused", "uw-pause__title"));
            _context = UiBuild.Text(string.Empty, UiClasses.Small, UiClasses.Muted, "uw-pause__context");
            head.Add(_context);
            _panel.Add(head);

            _list = UiBuild.Box("uw-pause__list");
            _list.Add(UiBuild.Button("Resume", () => _resume?.Invoke(), UiClasses.ButtonPrimary));
            _list.Add(UiBuild.Button("Main menu", OnBackToMenu, UiClasses.ButtonGhost));
            _list.Add(UiBuild.Button("Quit game", OnQuit, UiClasses.ButtonGhost));
            _panel.Add(_list);

            var hint = UiBuild.Box("uw-pause__hint", PickingMode.Ignore);
            var key = UiBuild.Box("uw-keycap", PickingMode.Ignore);
            key.AddToClassList("uw-keycap--small");
            key.Add(UiBuild.Text("Esc", "uw-keycap__label"));
            hint.Add(key);
            hint.Add(UiBuild.Text("to return to the game", UiClasses.Small, UiClasses.Muted));
            _panel.Add(hint);
            Root.Add(_panel);
        }

        public bool IsVisible => _visible;

        public void Show()
        {
            if (_visible) return;
            _visible = true;
            string area = HudBus.Area;
            string host = HudBus.HostAddress;
            if (!string.IsNullOrEmpty(host))
                _context.text = $"Hosting · friends join at {host}";
            else
                _context.text = string.IsNullOrEmpty(area) ? "The world keeps moving" : area + " · the world keeps moving";
            Root.style.display = DisplayStyle.Flex;
            UiAnim.Set(Root, true);
            UiAnim.In(_scrim, 0.2f);
            UiAnim.In(_panel, 0.22f, 0f, new Vector2(0f, 8f), UiAnim.PopoverScale);
            UiAnim.StaggerChildren(_list, 0.05f, 6f, UiAnim.FastEnterTime);
            // Tastiera e gamepad: il primo pulsante ha il fuoco.
            _list[0].schedule.Execute(() => _list[0].Focus()).StartingIn(20);
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            float t = UiAnim.ExitTime(0.22f);
            UiAnim.Out(_scrim, t, default, 1f, false);
            UiAnim.Out(_panel, t, new Vector2(0f, 6f), 0.98f, false, () =>
            {
                if (!_visible) Root.style.display = DisplayStyle.None;
            });
        }

        static void OnBackToMenu()
        {
            var l = GameServices.Launcher;
            if (l != null) l.Disconnect();
            else UiRoot.Instance?.FallbackToMenu();
        }

        static void OnQuit()
        {
            var l = GameServices.Launcher;
            if (l != null) l.QuitGame();
            else Application.Quit();
        }
    }
}
