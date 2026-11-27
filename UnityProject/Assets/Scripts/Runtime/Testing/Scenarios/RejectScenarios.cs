using System.Collections;
using Underworld.Core;
using Underworld.Net;
using Underworld.Player;

namespace Underworld.Testing.Scenarios
{
    /// <summary>
    /// Client che deve essere rifiutato (reject_bad_name, server_full).
    /// -expect-reason &lt;codice&gt; (DisconnectReasons). Controlla stato Failed, codice e testo in italiano.
    /// </summary>
    [AutoTestScenario("expect_reject")]
    public sealed class ExpectRejectScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            string expected = ctx.Arg("expect-reason", DisconnectReasons.NameInvalidCode);
            var launcher = ctx.Launcher;
            if (!ctx.Check("launcher presente", launcher != null)) yield break;

            yield return ctx.WaitUntil(() => launcher.State == LauncherState.Failed || PlayerNetwork.Local != null, 40f, "rifiuto del server");
            ctx.Check("il server rifiuta il client", launcher.State == LauncherState.Failed && PlayerNetwork.Local == null,
                $"stato {launcher.State} - {launcher.StatusText}");
            ctx.Check($"motivo '{expected}'", launcher.LastReasonCode == expected, $"codice ricevuto: {launcher.LastReasonCode ?? "nessuno"}");

            string prefix = ExpectedTextPrefix(expected);
            bool textOk = !string.IsNullOrEmpty(launcher.StatusText) &&
                          (prefix == null || launcher.StatusText.StartsWith(prefix)) &&
                          launcher.StatusText.IndexOf('|') < 0;
            ctx.Check("il client mostra il motivo in inglese", textOk, launcher.StatusText);
        }

        /// <summary>Inizio del testo in inglese atteso per alcuni codici.</summary>
        static string ExpectedTextPrefix(string code)
        {
            if (code == DisconnectReasons.NameInvalidCode) return "Invalid name";
            if (code == DisconnectReasons.ServerFullCode) return "Server is full";
            if (code == DisconnectReasons.IdentityInvalidCode) return "Your player profile could not be verified";
            return null;
        }
    }

    /// <summary>Client che entra e resta in gioco finché Tools/test.ps1 lo rilascia (server_full).</summary>
    [AutoTestScenario("stay_in_game")]
    public sealed class StayInGameScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return ctx.WaitForLocalPlayer();
            if (!ctx.LastWaitOk) yield break;
            yield return ctx.WaitForRelease(90f);
            ctx.Check("resto in gioco fino al rilascio", ctx.LastWaitOk && PlayerNetwork.Local != null);
        }
    }

    /// <summary>server_full, server: 2 giocatori dentro e almeno un rifiuto "server pieno".</summary>
    [AutoTestScenario("server_full.server")]
    public sealed class ServerFullServerScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var server = ctx.Server;
            ctx.Check("limite del server = 2", server.MaxPlayers == 2, $"limite {server.MaxPlayers}");
            yield return ctx.WaitUntil(() => ServerPlayerRegistry.JoinedCount == 2, 60f, "due giocatori");
            ctx.Check("2 giocatori in gioco", ctx.LastWaitOk, ConnectTwoClientsServer.RegisteredNames());
            yield return ctx.WaitUntil(() => server.RejectionCount(DisconnectReasons.ServerFullCode) >= 1, 60f, "rifiuto server pieno");
            ctx.Check("terzo client rifiutato: server pieno", ctx.LastWaitOk, $"rifiuti: {server.RejectionCount(DisconnectReasons.ServerFullCode)}");
            ctx.Check("sempre 2 giocatori registrati", ServerPlayerRegistry.Count == 2, $"registrati: {ServerPlayerRegistry.Count}");
        }
    }
}
