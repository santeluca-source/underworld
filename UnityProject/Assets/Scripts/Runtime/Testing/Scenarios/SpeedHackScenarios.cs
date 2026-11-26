using System.Collections;
using Underworld.Net;
using Underworld.Player;
using UnityEngine;

namespace Underworld.Testing.Scenarios
{
    /// <summary>
    /// speed_hack_corrected, client: un trucco SOLO DI TEST (DebugSpeedMultiplier, acceso solo qui) fa correre
    /// il personaggio a circa 30 m/s. Il server deve accorgersene e rimetterlo all'ultima posizione valida.
    /// Poi, senza trucco, la corsa normale non deve causare correzioni.
    /// </summary>
    [AutoTestScenario("speed_hack_corrected.client")]
    public sealed class SpeedHackClientScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return ctx.WaitForLocalPlayer();
            if (!ctx.LastWaitOk) yield break;
            var me = ctx.LocalPlayer;
            var validator = me.Validator;
            var controller = me.Controller;
            var bot = ctx.Bot;
            yield return ctx.Wait(1f);

            Vector3 start = me.transform.position;
            float farthest = 0f;
            bool corrected = false;
            Vector3 target = Vector3.zero;
            void OnCorrection(Vector3 position, int sequence)
            {
                if (corrected) return;
                corrected = true;
                target = position;
                // Trucco spento appena il server corregge.
                controller.DebugSpeedMultiplier = 1f;
                bot.Stop();
            }
            validator.CorrectionApplied += OnCorrection;

            controller.DebugSpeedMultiplier = 4f; // 7,5 m/s × 4 = 30 m/s
            bot.SetMove(ScenarioUtil.OutwardDirection(start), true);
            yield return ctx.WaitUntil(() =>
            {
                farthest = Mathf.Max(farthest, ScenarioUtil.Horizontal(me.transform.position, start));
                return corrected;
            }, 8f, "correzione del server");
            controller.DebugSpeedMultiplier = 1f;
            bot.Stop();
            validator.CorrectionApplied -= OnCorrection;

            if (!ctx.Check("il server corregge il trucco di velocità", corrected, $"distanza massima {farthest:0.0} m")) yield break;
            float targetFromStart = ScenarioUtil.Horizontal(target, start);
            ctx.Check("rimesso indietro rispetto al punto più lontano", farthest - targetFromStart >= 3f,
                $"punto più lontano {farthest:0.0} m, punto corretto {targetFromStart:0.0} m dalla partenza");

            yield return ctx.Wait(1f);
            float off = ScenarioUtil.Horizontal(me.transform.position, target);
            ctx.Check("resto nel punto corretto", off < 1.5f, $"distanza dal punto corretto {off:0.00} m");

            // Corsa normale: nessuna nuova correzione.
            int before = validator.CorrectionsReceived;
            bot.SetMove(-ScenarioUtil.OutwardDirection(start), true);
            yield return ctx.Wait(3f);
            bot.Stop();
            yield return ctx.Wait(1f);
            ctx.Check("nessuna correzione nella corsa normale", validator.CorrectionsReceived == before,
                $"correzioni prima {before}, dopo {validator.CorrectionsReceived}");
        }
    }

    /// <summary>speed_hack_corrected, server: vede la violazione e il giocatore torna al punto valido.</summary>
    [AutoTestScenario("speed_hack_corrected.server")]
    public sealed class SpeedHackServerScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return ctx.WaitUntil(() => ServerPlayerRegistry.JoinedCount == 1, 60f, "giocatore");
            if (!ctx.Check("un giocatore in gioco", ctx.LastWaitOk)) yield break;
            var entry = ServerPlayerRegistry.All[0];
            var validator = entry.PlayerObject != null ? entry.PlayerObject.GetComponent<MovementValidator>() : null;
            if (!ctx.Check("MovementValidator sul giocatore", validator != null)) yield break;

            yield return ctx.WaitUntil(() => validator == null || validator.ViolationCount >= 1, 40f, "violazione");
            if (!ctx.Check("violazione di velocità rilevata", validator != null && validator.ViolationCount >= 1,
                    validator != null ? $"violazioni {validator.ViolationCount}" : "giocatore uscito")) yield break;
            yield return ctx.WaitUntil(() => validator == null || !validator.AwaitingCorrection, 10f, "ritorno al punto valido");
            ctx.Check("il giocatore torna al punto valido", validator != null && !validator.AwaitingCorrection,
                validator != null ? $"correzioni mandate {validator.CorrectionsSent}" : "giocatore uscito");
            yield return ctx.WaitUntil(() => ServerPlayerRegistry.Count == 0, 60f, "uscita del client");
        }
    }
}
