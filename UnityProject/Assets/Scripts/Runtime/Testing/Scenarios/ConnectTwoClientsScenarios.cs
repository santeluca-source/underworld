using System;
using System.Collections;
using System.Collections.Generic;
using Underworld.Net;
using Underworld.Player;
using UnityEngine;

namespace Underworld.Testing.Scenarios
{
    /// <summary>Aiuti comuni degli scenari con più giocatori.</summary>
    static class ScenarioUtil
    {
        /// <summary>Nomi attesi da -expect-names "Alfa,Beta".</summary>
        public static List<string> ExpectedNames(AutoTestContext ctx)
        {
            var list = new List<string>();
            foreach (var n in (ctx.Arg("expect-names", "") ?? "").Split(','))
                if (!string.IsNullOrWhiteSpace(n)) list.Add(n.Trim());
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>Vero se questo processo vede esattamente i personaggi con questi nomi.</summary>
        public static bool SeesExactly(List<string> names)
        {
            var all = PlayerNetwork.All;
            if (all.Count != names.Count) return false;
            foreach (var p in all)
                if (p == null || !names.Contains(p.Name)) return false;
            return true;
        }

        public static float Horizontal(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Direzione verso l'esterno dello spawn (lontano dagli altri giocatori), sul piano.</summary>
        public static Vector3 OutwardDirection(Vector3 position)
        {
            var d = new Vector3(position.x, 0f, position.z);
            return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
        }
    }

    /// <summary>
    /// connect_two_clients, client che cammina: aspetta di vedere i 2 giocatori, cammina circa 10 m,
    /// poi aspetta che l'altro client esca.
    /// </summary>
    [AutoTestScenario("connect_two_clients.walker")]
    public sealed class ConnectTwoClientsWalker : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var names = ScenarioUtil.ExpectedNames(ctx);
            yield return ctx.WaitForLocalPlayer();
            if (!ctx.LastWaitOk) yield break;

            yield return ctx.WaitUntil(() => ScenarioUtil.SeesExactly(names), 30f, "due giocatori");
            if (!ctx.Check("vedo 2 giocatori con i nomi giusti", ctx.LastWaitOk, AutoTestContext.VisiblePlayerNames())) yield break;

            // Lascio all'altro client il tempo di prendere la posizione di partenza.
            yield return ctx.Wait(1.5f);
            var me = ctx.LocalPlayer;
            Vector3 start = me.transform.position;
            ctx.Bot.SetMove(ScenarioUtil.OutwardDirection(start), true);
            yield return ctx.WaitUntil(() => ScenarioUtil.Horizontal(me.transform.position, start) >= 10.5f, 8f, "camminata di 10 m");
            ctx.Bot.Stop();
            float walked = ScenarioUtil.Horizontal(me.transform.position, start);
            ctx.Check("ho camminato circa 10 m", walked >= 10f, $"{walked:0.0} m");
            ctx.Check("nessuna correzione del server camminando", me.Validator.CorrectionsReceived == 0,
                $"correzioni: {me.Validator.CorrectionsReceived}");

            // Resto in gioco finché l'altro client ha finito di guardare.
            yield return ctx.WaitUntil(() => PlayerNetwork.All.Count <= 1, 30f, "uscita dell'altro client");
        }
    }

    /// <summary>
    /// connect_two_clients, client che guarda: vede i 2 giocatori e controlla che quello indicato da -walker
    /// si sposti di almeno 8 m entro 10 s.
    /// </summary>
    [AutoTestScenario("connect_two_clients.watcher")]
    public sealed class ConnectTwoClientsWatcher : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var names = ScenarioUtil.ExpectedNames(ctx);
            string walkerName = ctx.Arg("walker", "Alfa");
            yield return ctx.WaitForLocalPlayer();
            if (!ctx.LastWaitOk) yield break;

            yield return ctx.WaitUntil(() => ScenarioUtil.SeesExactly(names), 30f, "due giocatori");
            if (!ctx.Check("vedo 2 giocatori con i nomi giusti", ctx.LastWaitOk, AutoTestContext.VisiblePlayerNames())) yield break;

            var walker = PlayerNetwork.FindByName(walkerName);
            if (!ctx.Check($"trovo il giocatore {walkerName}", walker != null)) yield break;
            Vector3 start = walker.transform.position;
            float t0 = ctx.Elapsed;
            yield return ctx.WaitUntil(() => walker != null && ScenarioUtil.Horizontal(walker.transform.position, start) >= 8f, 10f, "movimento dell'altro giocatore");
            float moved = walker != null ? ScenarioUtil.Horizontal(walker.transform.position, start) : 0f;
            ctx.Check($"vedo {walkerName} spostarsi di almeno 8 m entro 10 s", ctx.LastWaitOk,
                $"{moved:0.0} m in {ctx.Elapsed - t0:0.0} s");
        }
    }

    /// <summary>connect_two_clients, server: 2 client registrati con i nomi giusti; poi aspetta che escano.</summary>
    [AutoTestScenario("connect_two_clients.server")]
    public sealed class ConnectTwoClientsServer : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var names = ScenarioUtil.ExpectedNames(ctx);
            yield return ctx.WaitUntil(() => RegisteredNamesMatch(names), 60f, "due giocatori registrati");
            ctx.Check("2 client registrati con i nomi giusti", ctx.LastWaitOk, RegisteredNames());
            yield return ctx.WaitUntil(() => ServerPlayerRegistry.Count == 0, 60f, "uscita dei client");
            ctx.Check("i client sono usciti e il registro è vuoto", ctx.LastWaitOk, $"registrati: {ServerPlayerRegistry.Count}");
        }

        internal static bool RegisteredNamesMatch(List<string> names)
        {
            if (ServerPlayerRegistry.JoinedCount != names.Count) return false;
            foreach (var e in ServerPlayerRegistry.All)
                if (!e.Joined || !names.Contains(e.Name)) return false;
            return true;
        }

        internal static string RegisteredNames()
        {
            var list = new List<string>();
            foreach (var e in ServerPlayerRegistry.All) list.Add(e.ToString());
            return string.Join("; ", list);
        }
    }
}
