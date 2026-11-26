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

            float seconds = ctx.Args.GetInt("bot-seconds", 10);
            Vector3 last = me.transform.position;
            float path = 0f;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                Vector3 p = me.transform.position;
                path += ScenarioUtil.Horizontal(p, last);
                last = p;
                yield return null;
            }
            ctx.Check("il bot si muove", path > 5f, $"percorso {path:0.0} m in {seconds:0} s");
            ctx.Check("nessuna correzione del server", me.Validator.CorrectionsReceived == 0, $"correzioni {me.Validator.CorrectionsReceived}");
        }
    }
}
