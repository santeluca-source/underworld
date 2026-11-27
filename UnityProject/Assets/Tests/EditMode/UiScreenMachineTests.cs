using System.Collections.Generic;
using NUnit.Framework;
using Underworld.Core;
using Underworld.UI;

namespace Underworld.Tests
{
    /// <summary>Macchina a stati delle schermate (menu, HUD, caricamento, pausa, Esc) e coda degli avvisi.</summary>
    public class UiScreenMachineTests
    {
        [Test]
        public void Resolve_MenuSceneShowsMenu()
        {
            Assert.AreEqual(UiMode.Menu, UiModeRules.Resolve("MainMenu", null));
            Assert.AreEqual(UiMode.Menu, UiModeRules.Resolve("MainMenu", LauncherState.Idle));
            Assert.AreEqual(UiMode.Menu, UiModeRules.Resolve("MainMenu", LauncherState.Connecting));
            Assert.AreEqual(UiMode.Menu, UiModeRules.Resolve("MainMenu", LauncherState.Failed));
        }

        [Test]
        public void Resolve_InGameAlwaysShowsHud()
        {
            Assert.AreEqual(UiMode.Hud, UiModeRules.Resolve("MainMenu", LauncherState.InGame));
            Assert.AreEqual(UiMode.Hud, UiModeRules.Resolve("Bootstrap", LauncherState.InGame));
            Assert.AreEqual(UiMode.Hud, UiModeRules.Resolve("Area_Downtown", LauncherState.InGame));
        }

        [Test]
        public void Resolve_BusyOutsideMenuShowsLoading()
        {
            Assert.AreEqual(UiMode.Loading, UiModeRules.Resolve("Bootstrap", LauncherState.Connecting));
            Assert.AreEqual(UiMode.Loading, UiModeRules.Resolve("Area_Downtown", LauncherState.Transferring));
            Assert.AreEqual(UiMode.Loading, UiModeRules.Resolve("Area_Industrial", LauncherState.Failed));
        }

        [Test]
        public void Resolve_GameSceneWithoutLauncherShowsHud()
        {
            Assert.AreEqual(UiMode.Hud, UiModeRules.Resolve("Area_TestGround", null));
            Assert.AreEqual(UiMode.Hud, UiModeRules.Resolve("Downtown_Test", null));
            Assert.AreEqual(UiMode.None, UiModeRules.Resolve("Bootstrap", null));
            Assert.AreEqual(UiMode.None, UiModeRules.Resolve("Bootstrap", LauncherState.Idle));
        }

        [Test]
        public void Machine_RaisesModeChangedOnce()
        {
            var m = new UiScreenMachine();
            var seen = new List<UiMode>();
            m.ModeChanged += (_, n) => seen.Add(n);
            Assert.IsTrue(m.SetContext("MainMenu", null));
            Assert.IsFalse(m.SetContext("MainMenu", LauncherState.Idle));
            Assert.IsTrue(m.SetContext("MainMenu", LauncherState.InGame));
            CollectionAssert.AreEqual(new[] { UiMode.Menu, UiMode.Hud }, seen);
        }

        [Test]
        public void Pause_OnlyInHud_AndClosedWhenLeaving()
        {
            var m = new UiScreenMachine();
            m.SetContext("MainMenu", null);
            Assert.IsFalse(m.SetPause(true), "Nel menu la pausa non si apre");
            Assert.IsFalse(m.PauseOpen);

            m.SetContext("Area_Downtown", LauncherState.InGame);
            Assert.IsFalse(m.BlocksGameplay, "In gioco senza pausa il personaggio si muove");
            Assert.IsTrue(m.SetPause(true));
            Assert.IsTrue(m.PauseOpen);
            Assert.IsTrue(m.BlocksGameplay);

            bool closed = false;
            m.PauseChanged += open => closed |= !open;
            m.SetContext("MainMenu", LauncherState.Idle);
            Assert.IsFalse(m.PauseOpen);
            Assert.IsTrue(closed);
        }

        [Test]
        public void Escape_ClosesWindowFirst_ThenTogglesPause()
        {
            var m = new UiScreenMachine();
            m.SetContext("Area_Downtown", LauncherState.InGame);
            Assert.AreEqual(EscapeAction.CloseWindow, m.Escape(1));
            Assert.AreEqual(EscapeAction.OpenPause, m.Escape(0));
            m.SetPause(true);
            Assert.AreEqual(EscapeAction.ClosePause, m.Escape(0));
            Assert.AreEqual(EscapeAction.CloseWindow, m.Escape(2));
        }

        [Test]
        public void Escape_InMenuGoesBack()
        {
            var m = new UiScreenMachine();
            m.SetContext("MainMenu", null);
            Assert.AreEqual(EscapeAction.MenuBack, m.Escape(0));
            Assert.IsTrue(m.BlocksGameplay, "Il menu tiene libero il mouse");
            m.SetContext("Bootstrap", null);
            Assert.AreEqual(EscapeAction.None, m.Escape(0));
        }

        [Test]
        public void Toasts_KeepAtMostThree_NewestFirst()
        {
            var q = new ToastQueue();
            q.Push("a", ToastKind.Info, out var d1);
            q.Push("b", ToastKind.Success, out var d2);
            q.Push("c", ToastKind.Warning, out var d3);
            Assert.IsNull(d1); Assert.IsNull(d2); Assert.IsNull(d3);
            q.Push("d", ToastKind.Error, out var dropped);
            Assert.IsNotNull(dropped);
            Assert.AreEqual("a", dropped.Text);
            Assert.AreEqual(ToastQueue.MaxVisible, q.Items.Count);
            Assert.AreEqual("d", q.Items[0].Text);
        }

        [Test]
        public void Toasts_ExpireByKind()
        {
            var q = new ToastQueue();
            q.Push("info", ToastKind.Info, out _);
            q.Push("errore", ToastKind.Error, out _);
            var expired = new List<ToastQueue.Entry>();
            q.Tick(4.5f, expired);
            Assert.AreEqual(1, expired.Count);
            Assert.AreEqual("info", expired[0].Text);
            q.Tick(2f, expired);
            Assert.AreEqual(0, q.Items.Count);
        }

        [Test]
        public void Prompt_StripsKeyPrefix()
        {
            Assert.AreEqual("Apri il garage", HudScreen.CleanPrompt("E — Apri il garage"));
            Assert.AreEqual("Apri il garage", HudScreen.CleanPrompt("[E] Apri il garage"));
            Assert.AreEqual("Entra", HudScreen.CleanPrompt("Entra"));
        }
    }
}
