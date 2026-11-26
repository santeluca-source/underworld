using System.Collections;
using Underworld.Core;
using Underworld.Player;

namespace Underworld.Testing.Scenarios
{
    /// <summary>
    /// local_play_launcher, client: chiama GameLauncher.PlayLocal (con -localport, -localdatadir, -localarea),
    /// arriva in gioco e poi chiude. Il pid del server figlio va nel risultato (data.childPid):
    /// Tools/test.ps1 controlla che il server figlio si chiuda entro 10 s.
    /// </summary>
    [AutoTestScenario("local_play_launcher.client")]
    public sealed class LocalPlayLauncherScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var launcher = ctx.Launcher;
            if (!ctx.Check("launcher registrato in GameServices", launcher != null && ReferenceEquals(GameServices.Launcher, launcher))) yield break;

            launcher.PlayLocal(ctx.Args.PlayerName);
            yield return ctx.WaitUntil(() => launcher.State == LauncherState.InGame || launcher.State == LauncherState.Failed, 50f, "partita locale");
            ctx.Check("arrivo in gioco con Gioca", launcher.State == LauncherState.InGame, $"{launcher.State} - {launcher.StatusText}");
            ctx.Check("personaggio locale presente", PlayerNetwork.Local != null);
            int pid = launcher.ChildServerPid;
            ctx.SetData("childPid", pid);
            ctx.SetData("childDataDir", launcher.ChildServerDataDir);
            ctx.Check("server figlio acceso", pid != 0, $"pid {pid}");
            yield return ctx.Wait(1f);
        }
    }
}
