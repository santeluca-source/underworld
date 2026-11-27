using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Underworld.Core;
using Underworld.Net;
using Underworld.Player;
using Underworld.UI;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
using Debug = UnityEngine.Debug;

namespace Underworld.Testing.Scenarios
{
    /// <summary>Aiuti comuni degli scenari in Downtown (punti speciali, percorsi, screenshot, misure).</summary>
    static class DowntownUtil
    {
        public const string BoardId = "downtown.mission.board";

        /// <summary>Punto di spawn più vicino a una posizione (null se l'area non ne ha).</summary>
        public static AreaAnchor NearestSpawn(Vector3 p, out float distance)
        {
            AreaAnchor best = null;
            distance = float.MaxValue;
            foreach (var a in AreaAnchors.OfKind(AnchorKind.Spawn))
            {
                float d = ScenarioUtil.Horizontal(a.transform.position, p);
                if (d < distance) { distance = d; best = a; }
            }
            return best;
        }

        /// <summary>Centro dell'anello degli spawn (la fontana della piazza).</summary>
        public static Vector3 SpawnRingCenter()
        {
            var list = AreaAnchors.OfKind(AnchorKind.Spawn);
            Vector3 sum = Vector3.zero;
            foreach (var a in list) sum += a.transform.position;
            return list.Count > 0 ? sum / list.Count : Vector3.zero;
        }

        /// <summary>
        /// Direzione per andare verso <paramref name="target"/> senza passare sopra la fontana:
        /// se il bersaglio è dall'altra parte, gira lungo l'anello (raggio ~8,5 m), poi va dritto.
        /// </summary>
        public static Vector3 SteerAroundFountain(Vector3 p, Vector3 center, Vector3 target)
        {
            Vector3 fromC = p - center; fromC.y = 0f;
            Vector3 toT = target - center; toT.y = 0f;
            Vector3 straight = target - p; straight.y = 0f;
            if (fromC.magnitude > 12f || Vector3.Angle(fromC, toT) < 30f) return straight.normalized;
            float side = Mathf.Sign(Vector3.Cross(fromC, toT).y);
            Vector3 tangent = Vector3.Cross(Vector3.up, fromC.normalized) * side;
            Vector3 radial = fromC.normalized * Mathf.Clamp(8.6f - fromC.magnitude, -1f, 1f);
            return (tangent + radial).normalized;
        }

        /// <summary>Cammina (bot Scripted) fino a <paramref name="stopDistance"/> dal bersaglio.</summary>
        public static IEnumerator WalkTo(AutoTestContext ctx, PlayerNetwork me, Vector3 target, float stopDistance, float timeout)
        {
            Vector3 center = SpawnRingCenter();
            float until = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < until && ScenarioUtil.Horizontal(me.transform.position, target) > stopDistance)
            {
                me.Bot.SetMove(SteerAroundFountain(me.transform.position, center, target), false);
                yield return null;
            }
            me.Bot.Stop();
        }

        /// <summary>Salva uno screenshot in -shots (default: cartella dei log) e aspetta che il file esista.</summary>
        public static IEnumerator Shot(AutoTestContext ctx, string name)
        {
            string dir = ctx.Arg("shots", Path.GetTempPath());
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 5f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            ctx.Check("screenshot " + name, File.Exists(path), path);
        }

        /// <summary>Simula un tasto vero dell'Input System (premuto un frame, poi rilasciato).</summary>
        public static IEnumerator PressKey(Key key)
        {
            var kb = Keyboard.current;
            if (kb == null) yield break;
            // La finestra del test può non avere il fuoco (schermo 2): l'Input System spegnerebbe la tastiera.
            if (InputSystem.settings.backgroundBehavior != InputSettings.BackgroundBehavior.IgnoreFocus)
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            if (!kb.enabled) InputSystem.EnableDevice(kb);
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
        }

        /// <summary>FPS medi e batch massimi della camera su alcuni secondi (client con grafica).</summary>
        public static IEnumerator Measure(AutoTestContext ctx, string prefix, float seconds)
        {
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            int frames = 0;
            long maxBatches = 0, maxTris = 0;
            float worst = 0f;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return null;
                frames++;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                if (batches.Valid) maxBatches = Math.Max(maxBatches, batches.LastValue);
                if (tris.Valid) maxTris = Math.Max(maxTris, tris.LastValue);
            }
            float fps = frames / Mathf.Max(0.001f, Time.realtimeSinceStartup - t0);
            ctx.SetData(prefix + "Fps", Mathf.Round(fps));
            ctx.SetData(prefix + "WorstFrameMs", Mathf.Round(worst * 1000f));
            ctx.SetData(prefix + "MaxBatches", batches.Valid ? maxBatches : -1);
            ctx.SetData(prefix + "MaxTriangles", tris.Valid ? maxTris : -1);
            ctx.Log($"Misure {prefix}: fps={fps:0} peggior frame={worst * 1000f:0} ms batch={(batches.Valid ? maxBatches.ToString() : "n/d")} triangoli={(tris.Valid ? maxTris.ToString() : "n/d")} schermo={Screen.width}x{Screen.height}");
        }

        /// <summary>Controlla che il personaggio sia partito da uno spawn di Downtown.</summary>
        public static void CheckDowntownSpawn(AutoTestContext ctx, PlayerNetwork me)
        {
            var spawn = NearestSpawn(me.SpawnPosition, out float fromAnchor);
            ctx.Check("spawn su un punto downtown.spawn.*", spawn != null && spawn.Id.StartsWith("downtown.spawn.", StringComparison.Ordinal) && fromAnchor < 1f,
                spawn != null ? $"{spawn.Id} a {fromAnchor:0.00} m" : "nessun punto di spawn");
            float start = ScenarioUtil.Horizontal(me.transform.position, me.SpawnPosition);
            ctx.Check("personaggio sul suo spawn", start < 1f, $"posizione {me.transform.position}, spawn {me.SpawnPosition}");
        }

        /// <summary>La camera principale segue il personaggio locale.</summary>
        public static void CheckCameraFollows(AutoTestContext ctx, PlayerNetwork me)
        {
            var cam = Camera.main;
            var tpc = cam != null ? cam.GetComponent<ThirdPersonCamera>() : null;
            float d = cam != null ? Vector3.Distance(cam.transform.position, me.transform.position) : -1f;
            ctx.Check("la camera segue il personaggio", tpc != null && tpc.target == me.transform && d > 0.5f && d < 12f, $"distanza {d:0.0} m");
        }

        public static PlayerNetwork Other(PlayerNetwork me)
        {
            foreach (var p in PlayerNetwork.All)
                if (p != null && p != me) return p;
            return null;
        }
    }

    /// <summary>downtown_two_players, server: aspetta i 2 giocatori e misura memoria e CPU del processo.</summary>
    [AutoTestScenario("downtown_two_players.server")]
    public sealed class DowntownTwoPlayersServer : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            ctx.Check("area del server = Downtown", ctx.Server != null && ctx.Server.Area == AreaId.Downtown, ctx.Server != null ? ctx.Server.Area.ToString() : "nessun server");
            ctx.Check("punti di spawn di Downtown nel mondo del server", AreaAnchors.OfKind(AnchorKind.Spawn).Count >= 8, $"{AreaAnchors.OfKind(AnchorKind.Spawn).Count} punti");
            yield return ctx.WaitUntil(() => ServerPlayerRegistry.JoinedCount == 2, 90f, "due giocatori");
            if (!ctx.Check("2 giocatori in gioco sul server", ctx.LastWaitOk)) yield break;

            // Misura su 10 s con i due giocatori dentro.
            var proc = Process.GetCurrentProcess();
            using var sysMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            TimeSpan cpu0 = proc.TotalProcessorTime;
            float t0 = Time.realtimeSinceStartup;
            int frames = 0;
            while (Time.realtimeSinceStartup - t0 < 10f && ServerPlayerRegistry.JoinedCount > 0) { frames++; yield return null; }
            proc.Refresh();
            float wall = Time.realtimeSinceStartup - t0;
            double cpuPct = (proc.TotalProcessorTime - cpu0).TotalSeconds / Math.Max(0.001, wall) * 100.0;
            long systemUsed = sysMem.Valid ? sysMem.LastValue : 0;
            long reserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
            ctx.SetData("serverSystemUsedMB", Math.Round(systemUsed / 1048576.0));
            ctx.SetData("serverUnityReservedMB", Math.Round(reserved / 1048576.0));
            ctx.SetData("serverManagedMB", Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1));
            ctx.SetData("serverCpuPercentOneCore", Math.Round(cpuPct, 1));
            ctx.SetData("serverFrameRate", Math.Round(frames / Math.Max(0.001, wall)));
            ctx.Log($"Misure server: memoria di sistema {systemUsed / 1048576} MB, riservata da Unity {reserved / 1048576} MB, CPU {cpuPct:0.0}% di un core, {frames / wall:0} frame/s");
        }
    }

    /// <summary>
    /// downtown_two_players, giocatore B (senza grafica): parte da uno spawn, va davanti alla bacheca
    /// delle missioni e resta lì finché A ha finito (holdUntilOthersDone).
    /// </summary>
    [AutoTestScenario("downtown_two_players.walker")]
    public sealed class DowntownTwoPlayersWalker : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return ctx.WaitForLocalPlayer();
            if (!ctx.LastWaitOk) yield break;
            var me = ctx.LocalPlayer;
            DowntownUtil.CheckDowntownSpawn(ctx, me);
            yield return ctx.WaitUntil(() => PlayerNetwork.All.Count == 2, 60f, "l'altro giocatore");
            ctx.Check("B vede 2 giocatori", ctx.LastWaitOk, AutoTestContext.VisiblePlayerNames());

            var board = AreaAnchors.Find(DowntownUtil.BoardId);
            if (!ctx.Check("bacheca delle missioni trovata", board != null)) yield break;
            yield return DowntownUtil.WalkTo(ctx, me, board.transform.position, 1.2f, 25f);
            float d = ScenarioUtil.Horizontal(me.transform.position, board.transform.position);
            ctx.Check("B arriva davanti alla bacheca", d < 2.5f, $"distanza {d:0.0} m");
            ctx.Check("B: nessuna correzione del server", me.Validator.CorrectionsReceived == 0, $"correzioni {me.Validator.CorrectionsReceived}");
            yield return ctx.WaitForRelease(90f);
        }
    }

    /// <summary>
    /// downtown_two_players, giocatore A (con grafica): spawn, HUD, camera; vede B; cammina verso la bacheca;
    /// screenshot con B e l'HUD; misure di FPS e batch; poi Esc → pausa (screenshot) → Esc.
    /// </summary>
    [AutoTestScenario("downtown_two_players.viewer")]
    public sealed class DowntownTwoPlayersViewer : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return ctx.WaitForLocalPlayer();
            if (!ctx.LastWaitOk) yield break;
            var me = ctx.LocalPlayer;
            ctx.Check("scena Area_Downtown", SceneManager.GetActiveScene().name == AreaInfo.SceneName(AreaId.Downtown), SceneManager.GetActiveScene().name);
            DowntownUtil.CheckDowntownSpawn(ctx, me);
            yield return ctx.Wait(0.5f);
            DowntownUtil.CheckCameraFollows(ctx, me);
            var ui = UiRoot.Instance;
            ctx.Check("HUD in gioco", ui != null && ui.Machine.Mode == UiMode.Hud && !ui.Machine.PauseOpen, ui != null ? ui.Machine.Mode.ToString() : "nessuna UI");
            ctx.Check("nessun DevHud o Downtown_Test", GameObject.Find("DevHud") == null && SceneManager.GetSceneByName("Downtown_Test").IsValid() == false);

            yield return ctx.WaitUntil(() => PlayerNetwork.All.Count == 2, 60f, "l'altro giocatore");
            ctx.Check("A vede 2 giocatori", ctx.LastWaitOk, AutoTestContext.VisiblePlayerNames());
            var other = DowntownUtil.Other(me);
            var board = AreaAnchors.Find(DowntownUtil.BoardId);
            if (!ctx.Check("bacheca delle missioni trovata", board != null && other != null)) yield break;

            // Aspetta che B sia arrivato alla bacheca (posizione vista attraverso la rete).
            yield return ctx.WaitUntil(() => other != null && ScenarioUtil.Horizontal(other.transform.position, board.transform.position) < 2.5f, 30f, "B alla bacheca");
            ctx.Check("A vede B arrivare alla bacheca", ctx.LastWaitOk, other != null ? $"B in {other.transform.position}" : "B uscito");

            float before = ScenarioUtil.Horizontal(me.transform.position, board.transform.position);
            yield return DowntownUtil.WalkTo(ctx, me, board.transform.position, 6f, 25f);
            float after = ScenarioUtil.Horizontal(me.transform.position, board.transform.position);
            ctx.Check("A cammina verso la bacheca", after < 7f && after < before - 0.5f, $"da {before:0.0} m a {after:0.0} m");
            ctx.Check("A: nessuna correzione del server", me.Validator.CorrectionsReceived == 0, $"correzioni {me.Validator.CorrectionsReceived}");

            // Inquadra B (e la bacheca dietro di lui) da dietro le spalle di A.
            if (other != null && Camera.main != null)
            {
                Vector3 dir = other.transform.position - me.transform.position;
                Camera.main.GetComponent<ThirdPersonCamera>()?.SetView(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 10f, 10f);
            }
            HudBus.SetObjective("Check the mission board");
            yield return ctx.Wait(1.2f);
            if (other != null && Camera.main != null)
            {
                Vector3 vp = Camera.main.WorldToViewportPoint(other.transform.position + Vector3.up);
                ctx.Check("B è nell'inquadratura", vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f, $"viewport {vp}");
            }
            yield return DowntownUtil.Shot(ctx, "a1-downtown-two-players");
            yield return DowntownUtil.Measure(ctx, "downtown", 5f);

            // Esc vero (Input System) → pausa; la camera non usa Esc e il mouse si libera.
            yield return DowntownUtil.PressKey(Key.Escape);
            yield return ctx.WaitUntil(() => ui.Machine.PauseOpen, 3f, "pausa con Esc");
            bool escOk = ctx.LastWaitOk;
            ctx.Check("Esc apre il menu di pausa", escOk);
            if (!escOk) ui.Machine.SetPause(true);
            yield return ctx.Wait(0.6f);
            ctx.Check("con la pausa il personaggio è fermo e il mouse libero", InputGate.GameplayBlocked && Cursor.lockState == CursorLockMode.None, $"bloccato {InputGate.GameplayBlocked}, cursore {Cursor.lockState}");
            yield return DowntownUtil.Shot(ctx, "a1-pause-ingame");
            yield return DowntownUtil.PressKey(Key.Escape);
            yield return ctx.WaitUntil(() => !ui.Machine.PauseOpen, 3f, "pausa chiusa con Esc");
            if (!ctx.Check("Esc chiude il menu di pausa", ctx.LastWaitOk)) ui.Machine.SetPause(false);
            yield return ctx.Wait(0.3f);
            ctx.Check("dopo la pausa i comandi tornano attivi", !InputGate.GameplayBlocked);
        }
    }

    /// <summary>
    /// menu_play_local, client con grafica e senza -connect: il menu vero (screenshot), nome nel campo,
    /// clic su "Play" → GameLauncher.PlayLocal (server figlio Downtown) → caricamento (screenshot) → InGame
    /// con HUD in Downtown (screenshot). Il nome salvato dell'utente si rimette a posto alla fine.
    /// </summary>
    [AutoTestScenario("menu_play_local.client")]
    public sealed class MenuPlayLocalScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            string savedName = PlayerPrefs.GetString(MainMenuScreen.PrefName, null);
            bool hadName = PlayerPrefs.HasKey(MainMenuScreen.PrefName);
            try
            {
                var e = RunInner(ctx);
                while (true)
                {
                    object cur;
                    try { if (!e.MoveNext()) break; cur = e.Current; }
                    catch (Exception ex) { ctx.Fail("eccezione: " + ex.Message); break; }
                    yield return cur;
                }
            }
            finally
            {
                if (hadName) PlayerPrefs.SetString(MainMenuScreen.PrefName, savedName);
                else PlayerPrefs.DeleteKey(MainMenuScreen.PrefName);
                PlayerPrefs.Save();
            }
        }

        IEnumerator RunInner(AutoTestContext ctx)
        {
            var launcher = ctx.Launcher;
            yield return ctx.WaitUntil(() => SceneManager.GetActiveScene().name == UiModeRules.MenuScene && UiRoot.Instance != null && UiRoot.Instance.Machine.Mode == UiMode.Menu, 20f, "menu principale");
            if (!ctx.Check("menu principale aperto", ctx.LastWaitOk, SceneManager.GetActiveScene().name)) yield break;
            ctx.Check("launcher registrato", launcher != null && ReferenceEquals(GameServices.Launcher, launcher));
            yield return ctx.Wait(2.5f); // entrata della UI e sfondo
            yield return DowntownUtil.Shot(ctx, "a1-main-menu");

            var root = UiRoot.Instance.RootElement;
            var field = root.Q<TextField>(className: "uw-textfield");
            Button play = null;
            root.Query<Button>().ForEach(b => { if (play == null && b.text == "Play") play = b; });
            if (!ctx.Check("campo del nome e pulsante Play", field != null && play != null)) yield break;
            field.value = ctx.Args.PlayerName ?? "MenuTester";

            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = play;
                play.SendEvent(submit);
            }
            yield return ctx.WaitUntil(() => launcher.State != LauncherState.Idle, 3f, "avvio dal pulsante Play");
            ctx.Check("Play avvia GameLauncher", ctx.LastWaitOk, launcher.State.ToString());
            yield return ctx.Wait(0.4f);
            if (launcher.State == LauncherState.StartingServer || launcher.State == LauncherState.Connecting)
                yield return DowntownUtil.Shot(ctx, "a1-play-local-loading");

            yield return ctx.WaitUntil(() => launcher.State == LauncherState.InGame || launcher.State == LauncherState.Failed, 50f, "partita locale");
            ctx.Check("arrivo in gioco (InGame)", launcher.State == LauncherState.InGame, $"{launcher.State} - {launcher.StatusText}");
            ctx.Check("status in inglese", launcher.StatusText != null && launcher.StatusText.StartsWith("Playing on your PC", StringComparison.Ordinal), launcher.StatusText);
            yield return ctx.WaitForLocalPlayer(20f);
            if (!ctx.LastWaitOk) yield break;
            var me = ctx.LocalPlayer;
            ctx.Check("server figlio in Area_Downtown", SceneManager.GetActiveScene().name == AreaInfo.SceneName(AreaId.Downtown), SceneManager.GetActiveScene().name);
            DowntownUtil.CheckDowntownSpawn(ctx, me);
            ctx.SetData("childPid", launcher.ChildServerPid);
            yield return ctx.Wait(2.5f); // banner dell'area
            DowntownUtil.CheckCameraFollows(ctx, me);
            ctx.Check("HUD in gioco", UiRoot.Instance.Machine.Mode == UiMode.Hud, UiRoot.Instance.Machine.Mode.ToString());
            yield return DowntownUtil.Shot(ctx, "a1-play-local-ingame");
            yield return ctx.Wait(0.5f);
        }
    }
}
