using System.Collections;
using Underworld.Player;
using UnityEngine;

namespace Underworld.Testing.Scenarios
{
    /// <summary>
    /// bot_wander, client avviato con -bot wander: il bot gira, corre e salta per alcuni secondi
    /// (-bot-seconds, default 10). Controlla che si muova e che il server non lo corregga mai
    /// (nessun falso allarme del MovementValidator con corse e salti veri).
    /// </summary>
    [AutoTestScenario("bot_wander.client")]
    public sealed class BotWanderScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return ctx.WaitForLocalPlayer();
            if (!ctx.LastWaitOk) yield break;
            var me = ctx.LocalPlayer;
            ctx.Check("bot in modo wander", me.Bot != null && me.Bot.Mode == BotMode.Wander, me.Bot != null ? me.Bot.Mode.ToString() : "nessun bot");

            // Regressione del vecchio blocco in (0,0,0): il personaggio deve partire dal punto di spawn
            // del server e non deve mai fare salti (un salto = capsula del CharacterController rimasta altrove).
            Vector3 start = me.transform.position;
            float fromSpawn = ScenarioUtil.Horizontal(start, me.SpawnPosition);
            ctx.Check("partenza dal punto di spawn", fromSpawn < 1f, $"posizione {start}, spawn {me.SpawnPosition}");

            float seconds = ctx.Args.GetInt("bot-seconds", 10);
            Vector3 last = start;
            float path = 0f;
            float maxStep = 0f;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                Vector3 p = me.transform.position;
                float step = ScenarioUtil.Horizontal(p, last);
                path += step;
                if (step > maxStep) maxStep = step;
                last = p;
                yield return null;
            }
            ctx.Check("il bot si muove", path > 5f, $"percorso {path:0.0} m in {seconds:0} s");
            // Uno sprint a 7,5 m/s fa meno di 1 m anche in un frame lento (passo massimo 0,1 s).
            ctx.Check("nessun salto di posizione", maxStep < 1.5f, $"passo massimo {maxStep:0.00} m in un frame");
            ctx.Check("nessuna correzione del server", me.Validator.CorrectionsReceived == 0, $"correzioni {me.Validator.CorrectionsReceived}");
        }
    }
}
