using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Underworld.Core;
using Underworld.Economy;
using Underworld.Inventory;
using Underworld.Net;
using Underworld.Player;
using Underworld.Server;
using UnityEngine;

namespace Underworld.Testing.Scenarios
{
    /// <summary>
    /// Aiuti comuni degli scenari delle Fasi 5–6 (identità, salvataggi, soldi, inventario).
    /// Argomenti: -expect-cash N, -expect-items "id=n,id=n" (vuoto = inventario vuoto), -set-cash N.
    /// </summary>
    internal static class PersistenceTest
    {
        // ---------------- Client ----------------

        /// <summary>Personaggio, portafoglio e inventario locali pronti.</summary>
        public static IEnumerator WaitReady(AutoTestContext ctx)
        {
            yield return ctx.WaitForLocalPlayer();
            if (!ctx.LastWaitOk) yield break;
            yield return ctx.WaitUntil(() => PlayerWallet.Local != null && PlayerWallet.Local.HasValue &&
                                             PlayerInventory.Local != null && PlayerTestHooks.Local != null, 15f, "portafoglio e inventario");
            ctx.Check("portafoglio, inventario e canale di test sul personaggio", ctx.LastWaitOk);
        }

        /// <summary>Manda un comando di test e aspetta la risposta.</summary>
        public static IEnumerator Hook(AutoTestContext ctx, string command, string arg, long value)
        {
            var hooks = PlayerTestHooks.Local;
            if (hooks == null) { ctx.Check($"comando {command}: canale di test", false); yield break; }
            int before = hooks.Replies;
            hooks.Send(command, arg, value);
            yield return ctx.WaitUntil(() => hooks.Replies > before, 10f, "risposta al comando " + command);
        }

        public static Dictionary<string, int> ParseItems(string spec)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(spec)) return d;
            foreach (var part in spec.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && int.TryParse(kv[1], out int n)) d[kv[0].Trim()] = n;
            }
            return d;
        }

        public static bool InventoryMatches(Dictionary<string, int> expected, out string detail)
        {
            var inv = PlayerInventory.Local;
            var sb = new StringBuilder();
            bool ok = inv != null && inv.SlotCount == expected.Count;
            if (inv != null)
                for (int i = 0; i < inv.SlotCount; i++) sb.Append(inv.GetSlot(i).ItemId.ToString()).Append('=').Append(inv.GetSlot(i).Count).Append(' ');
            foreach (var kv in expected) if (inv == null || inv.CountOf(kv.Key) != kv.Value) ok = false;
            detail = sb.Length > 0 ? sb.ToString().Trim() : "(vuoto)";
            return ok;
        }

        /// <summary>Controlla soldi (portafoglio e HUD) e inventario attesi dagli argomenti.</summary>
        public static IEnumerator CheckExpected(AutoTestContext ctx)
        {
            long cash = long.Parse(ctx.Arg("expect-cash", "5000"));
            var items = ParseItems(ctx.Arg("expect-items", ""));
            yield return ctx.WaitUntil(() => PlayerWallet.Local != null && PlayerWallet.Local.Cash == cash, 10f, "soldi attesi");
            ctx.Check($"soldi = ${cash:N0}", ctx.LastWaitOk, $"portafoglio {PlayerWallet.Local?.Cash}");
            ctx.Check("l'HUD mostra i soldi veri", HudBus.Cash == cash, $"HudBus.Cash = {HudBus.Cash}");
            string detail = null;
            yield return ctx.WaitUntil(() => InventoryMatches(items, out detail), 10f, "inventario atteso");
            ctx.Check($"inventario = '{ctx.Arg("expect-items", "")}'", ctx.LastWaitOk, detail);
            ctx.SetData("cash", PlayerWallet.Local?.Cash ?? -1);
            ctx.SetData("inventory", detail);
        }

        /// <summary>Aspetta che il server scolleghi questo client e controlla il motivo.</summary>
        public static IEnumerator ExpectKicked(AutoTestContext ctx, string code, string textPrefix, float timeout)
        {
            var launcher = ctx.Launcher;
            yield return ctx.WaitUntil(() => launcher != null && launcher.State == LauncherState.Failed, timeout, "disconnessione da parte del server");
            ctx.Check($"scollegato dal server con '{code}'", ctx.LastWaitOk && launcher.LastReasonCode == code,
                $"stato {launcher?.State}, codice {launcher?.LastReasonCode ?? "nessuno"}");
            ctx.Check("disconnessione durante la partita", launcher != null && launcher.DisconnectedInGame);
            if (textPrefix != null)
                ctx.Check("testo del motivo in inglese", launcher?.LastReasonText != null && launcher.LastReasonText.StartsWith(textPrefix),
                    launcher?.LastReasonText);
        }

        // ---------------- Server ----------------

        /// <summary>Conta gli ingressi nell'area (dall'inizio dello scenario).</summary>
        public sealed class JoinCounter
        {
            public int Joins;
            public string FirstPlayerId;
            public JoinCounter()
            {
                ServerPlayerRegistry.PlayerJoined += e =>
                {
                    Joins++;
                    FirstPlayerId ??= e.PlayerId;
                };
            }
        }

        /// <summary>Righe del registro dei movimenti di un giocatore (tutti i file del giorno, cartella dati condivisa).</summary>
        public static List<TransactionRecord> JournalFor(string playerId)
        {
            var list = new List<TransactionRecord>();
            var ctx = ServerContext.Current;
            if (ctx == null || playerId == null || !Directory.Exists(ctx.Journal.Dir)) return list;
            foreach (var file in Directory.GetFiles(ctx.Journal.Dir, "*.jsonl"))
            {
                string text;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
                foreach (var line in text.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var r = TransactionJournal.ParseLine(line);
                    if (r != null && r.PlayerId == playerId) list.Add(r);
                }
            }
            return list;
        }

        public static string Describe(List<TransactionRecord> rows)
        {
            var sb = new StringBuilder();
            foreach (var r in rows) sb.Append(r.Reason).Append(' ').Append(r.Delta).Append(" -> ").Append(r.Balance).Append("; ");
            return sb.ToString();
        }

        /// <summary>Salvataggio letto dal disco (null se manca o non è leggibile).</summary>
        public static PlayerSave LoadFromDisk(string playerId)
        {
            var ctx = ServerContext.Current;
            if (ctx == null || playerId == null) return null;
            var r = ctx.Store.Load(playerId);
            return r.HasSave ? r.Save : null;
        }

        public static IEnumerator WaitAllLeft(AutoTestContext ctx, float timeout)
        {
            yield return ctx.WaitUntil(() => ServerPlayerRegistry.Count == 0, timeout, "uscita di tutti i giocatori");
            ctx.Check("tutti i giocatori sono usciti", ctx.LastWaitOk, $"registrati: {ServerPlayerRegistry.Count}");
        }

        public static void CheckNoSaveErrors(AutoTestContext ctx)
        {
            var c = ServerContext.Current;
            ctx.Check("servizi del server pronti", c != null);
            if (c == null) return;
            ctx.Check("nessun salvataggio fallito", c.Sessions.SavesFailed == 0, $"riusciti {c.Sessions.SavesOk}, falliti {c.Sessions.SavesFailed}");
            ctx.SetData("savesOk", c.Sessions.SavesOk);
        }
    }

    // =====================================================================
    // persistence_roundtrip: entra, soldi e oggetti con i comandi di test, esce, rientra con lo stesso profilo.
    // =====================================================================

    /// <summary>Primo ingresso: profilo nuovo ($5,000), comandi di test, uso di un oggetto.</summary>
    [AutoTestScenario("persist.first")]
    public sealed class PersistFirstScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return PersistenceTest.WaitReady(ctx);
            if (!ctx.LastWaitOk) yield break;
            yield return ctx.WaitUntil(() => PlayerWallet.Local.Cash == Money.StartingCash, 5f);
            ctx.Check("profilo nuovo con $5,000", PlayerWallet.Local.Cash == Money.StartingCash, $"soldi {PlayerWallet.Local.Cash}");
            ctx.Check("HUD con $5,000", HudBus.Cash == Money.StartingCash, $"HudBus.Cash = {HudBus.Cash}");

            long target = long.Parse(ctx.Arg("set-cash", "12345"));
            yield return PersistenceTest.Hook(ctx, "set_cash", "", target);
            ctx.Check("comando set_cash eseguito (-testmode)", ctx.LastWaitOk && PlayerTestHooks.Local.LastOk, PlayerTestHooks.Local.LastResult);
            yield return PersistenceTest.Hook(ctx, "give_item", TestItems.Snack, 3);
            ctx.Check("comando give_item snack", ctx.LastWaitOk && PlayerTestHooks.Local.LastOk, PlayerTestHooks.Local.LastResult);
            yield return PersistenceTest.Hook(ctx, "give_item", TestItems.Rock, 1);
            ctx.Check("comando give_item rock", ctx.LastWaitOk && PlayerTestHooks.Local.LastOk, PlayerTestHooks.Local.LastResult);
            yield return PersistenceTest.Hook(ctx, "give_item", TestItems.Rock, 50);
            ctx.Check("oltre MaxStack: rifiutato", ctx.LastWaitOk && !PlayerTestHooks.Local.LastOk, PlayerTestHooks.Local.LastResult);

            var snackAndRock = new Dictionary<string, int> { [TestItems.Snack] = 3, [TestItems.Rock] = 1 };
            string detail = null;
            yield return ctx.WaitUntil(() => PersistenceTest.InventoryMatches(snackAndRock, out detail), 10f, "inventario sul client");
            ctx.Check("il client riceve l'inventario", ctx.LastWaitOk, detail);

            // Uso: lo snack ha un gestore (riesce e sparisce un pezzo), il sasso non è usabile.
            bool? lastOk = null;
            PlayerInventory.LocalUseResult += (ok, id, msg) => lastOk = ok;
            PlayerInventory.Local.RequestUse(TestItems.Snack);
            yield return ctx.WaitUntil(() => lastOk.HasValue, 10f, "risposta all'uso");
            ctx.Check("uso dello snack riuscito", lastOk == true);
            yield return ctx.WaitUntil(() => PlayerInventory.Local.CountOf(TestItems.Snack) == 2, 10f);
            ctx.Check("lo snack usato sparisce (3 → 2)", ctx.LastWaitOk, $"snack {PlayerInventory.Local.CountOf(TestItems.Snack)}");
            lastOk = null;
            PlayerInventory.Local.RequestUse(TestItems.Rock);
            yield return ctx.WaitUntil(() => lastOk.HasValue, 10f, "risposta all'uso del sasso");
            ctx.Check("il sasso non si può usare", lastOk == false);
            yield return ctx.Wait(0.5f);
            ctx.Check("il sasso resta (1)", PlayerInventory.Local.CountOf(TestItems.Rock) == 1);

            yield return ctx.WaitUntil(() => PlayerWallet.Local.Cash == target && HudBus.Cash == target, 10f);
            ctx.Check($"soldi ${target:N0} nel portafoglio e nell'HUD", ctx.LastWaitOk, $"portafoglio {PlayerWallet.Local.Cash}, HUD {HudBus.Cash}");
        }
    }

    /// <summary>Ingresso con un profilo che esiste: controlla soldi, HUD e inventario attesi (-expect-cash, -expect-items).</summary>
    [AutoTestScenario("persist.check")]
    public sealed class PersistCheckScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return PersistenceTest.WaitReady(ctx);
            if (!ctx.LastWaitOk) yield break;
            yield return PersistenceTest.CheckExpected(ctx);
            if (!string.IsNullOrEmpty(ctx.Arg("autotest-release"))) yield return ctx.WaitForRelease(90f);
        }
    }

    /// <summary>Server di persistence_roundtrip: due ingressi dello stesso giocatore, dati sul disco e nel registro.</summary>
    [AutoTestScenario("persist.server")]
    public sealed class PersistServerScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var joins = new PersistenceTest.JoinCounter();
            ctx.Check("comandi di test attivi (-testmode)", ServerTestHooks.Enabled);
            int expected = int.Parse(ctx.Arg("expect-joins", "2"));
            yield return ctx.WaitUntil(() => joins.Joins >= expected && ServerPlayerRegistry.Count == 0, 100f, "ingressi e uscite");
            ctx.Check($"{expected} ingressi dello stesso giocatore", ctx.LastWaitOk, $"ingressi {joins.Joins}");
            string pid = joins.FirstPlayerId;

            var save = PersistenceTest.LoadFromDisk(pid);
            ctx.Check("salvataggio sul disco", save != null);
            long cash = long.Parse(ctx.Arg("expect-cash", "12345"));
            ctx.Check($"soldi salvati = {cash}", save != null && save.Cash == cash, $"soldi {save?.Cash}");
            var items = PersistenceTest.ParseItems(ctx.Arg("expect-items", ""));
            bool itemsOk = save != null && save.Inventory.Count == items.Count;
            foreach (var kv in items) itemsOk &= save != null && save.Inventory.TryGetValue(kv.Key, out int n) && n == kv.Value;
            ctx.Check("inventario salvato", itemsOk);

            var rows = PersistenceTest.JournalFor(pid);
            ctx.Check("registro: soldi iniziali e comando di test", rows.Count >= 2 && rows[0].Reason == "account.start" && rows[0].Delta == Money.StartingCash
                && rows[rows.Count - 1].Balance == cash, PersistenceTest.Describe(rows));
            ctx.Check("lucchetto liberato dopo l'uscita", !File.Exists(ServerContext.Current.Locks.PathFor(pid)));
            ctx.Check("nessun file .tmp rimasto", !File.Exists(ServerContext.Current.Locks.PathFor(pid) + ".tmp") &&
                                                   Directory.GetFiles(Path.Combine(ServerContext.Current.DataDir, "players"), "*.tmp").Length == 0);
            PersistenceTest.CheckNoSaveErrors(ctx);
        }
    }

    // =====================================================================
    // identity_spoof_rejected: stesso playerId con un altro token, con il vero giocatore dentro e fuori.
    // =====================================================================

    [AutoTestScenario("spoof.server")]
    public sealed class SpoofServerScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var joins = new PersistenceTest.JoinCounter();
            var server = ctx.Server;
            yield return ctx.WaitUntil(() => joins.Joins >= 1, 60f, "il vero giocatore");
            string pid = joins.FirstPlayerId;
            string hash = ServerContext.Current.Sessions.TryGetByPlayerId(pid, out var s) ? s.Save.TokenHash : null;

            yield return ctx.WaitUntil(() => server.RejectionCount(DisconnectReasons.IdentityInvalidCode) >= 1, 60f, "primo finto");
            ctx.Check("finto rifiutato mentre il vero è in gioco", ctx.LastWaitOk);
            ctx.Check("il vero giocatore resta dentro", ServerPlayerRegistry.JoinedCount == 1 && ServerContext.Current.Sessions.TryGetByPlayerId(pid, out _));

            yield return ctx.WaitUntil(() => server.RejectionCount(DisconnectReasons.IdentityInvalidCode) >= 2, 60f, "secondo finto");
            ctx.Check("finto rifiutato anche con il vero fuori (hash sul disco)", ctx.LastWaitOk);

            yield return ctx.WaitUntil(() => joins.Joins >= 2 && ServerPlayerRegistry.Count == 0, 60f, "il vero giocatore rientra ed esce");
            ctx.Check("il vero giocatore rientra dopo i finti", ctx.LastWaitOk, $"ingressi {joins.Joins}");
            var save = PersistenceTest.LoadFromDisk(pid);
            ctx.Check("hash del token invariato", save != null && hash != null && save.TokenHash == hash);
            ctx.Check("nessuna sostituzione", server.RejectionCount(DisconnectReasons.AlreadyConnectedCode) == 0);
            PersistenceTest.CheckNoSaveErrors(ctx);
        }
    }

    // =====================================================================
    // session_replaced: lo stesso giocatore entra da un secondo client mentre il primo è dentro.
    // =====================================================================

    [AutoTestScenario("replaced.first")]
    public sealed class ReplacedFirstScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return PersistenceTest.WaitReady(ctx);
            if (!ctx.LastWaitOk) yield break;
            long target = long.Parse(ctx.Arg("set-cash", "7777"));
            yield return PersistenceTest.Hook(ctx, "set_cash", "", target);
            ctx.Check("soldi impostati", ctx.LastWaitOk && PlayerTestHooks.Local.LastOk);
            yield return PersistenceTest.ExpectKicked(ctx, DisconnectReasons.SessionReplacedCode, "You joined from another window", 60f);
        }
    }

    [AutoTestScenario("replaced.server")]
    public sealed class ReplacedServerScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var joins = new PersistenceTest.JoinCounter();
            var sessions = ServerContext.Current.Sessions;
            yield return ctx.WaitUntil(() => joins.Joins >= 2, 60f, "due ingressi");
            ctx.Check("il secondo client entra", ctx.LastWaitOk);
            yield return ctx.WaitUntil(() => ServerPlayerRegistry.Count == 1, 20f, "il primo client esce");
            ctx.Check("il primo client è scollegato, resta una sola sessione", ctx.LastWaitOk && sessions.Count == 1,
                $"registrati {ServerPlayerRegistry.Count}, sessioni {sessions.Count}");
            string pid = joins.FirstPlayerId;
            long cash = long.Parse(ctx.Arg("expect-cash", "7777"));
            ctx.Check("la sessione nuova ha i dati della vecchia", sessions.TryGetByPlayerId(pid, out var s) && s.Save.Cash == cash, $"soldi {s?.Save.Cash}");
            ctx.Check("nessun rifiuto already_connected", ctx.Server.RejectionCount(DisconnectReasons.AlreadyConnectedCode) == 0);
            yield return PersistenceTest.WaitAllLeft(ctx, 60f);
            var save = PersistenceTest.LoadFromDisk(pid);
            ctx.Check("dati intatti sul disco", save != null && save.Cash == cash, $"soldi {save?.Cash}");
            ctx.Check("lucchetto liberato", !File.Exists(ServerContext.Current.Locks.PathFor(pid)));
            PersistenceTest.CheckNoSaveErrors(ctx);
        }
    }

    // =====================================================================
    // server_restart_persistence: server.stop con il giocatore dentro, riavvio con la stessa cartella dati.
    // =====================================================================

    [AutoTestScenario("restart.first")]
    public sealed class RestartFirstScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return PersistenceTest.WaitReady(ctx);
            if (!ctx.LastWaitOk) yield break;
            yield return PersistenceTest.Hook(ctx, "set_cash", "", long.Parse(ctx.Arg("set-cash", "4242")));
            ctx.Check("soldi impostati", ctx.LastWaitOk && PlayerTestHooks.Local.LastOk);
            yield return PersistenceTest.Hook(ctx, "give_item", TestItems.Rock, 2);
            ctx.Check("oggetti dati", ctx.LastWaitOk && PlayerTestHooks.Local.LastOk);
            yield return PersistenceTest.ExpectKicked(ctx, DisconnectReasons.ServerClosingCode, "The server has closed", 90f);
        }
    }

    /// <summary>Prima del riavvio: aspetta i dati, poi cambia un dato senza salvarlo (lo salva solo ServerStopping).</summary>
    [AutoTestScenario("restart.before")]
    public sealed class RestartBeforeScenario : IAutoTestScenario
    {
        public const string ModuleKey = "test.restart";

        public IEnumerator Run(AutoTestContext ctx)
        {
            var joins = new PersistenceTest.JoinCounter();
            yield return ctx.WaitUntil(() => joins.Joins >= 1, 60f, "ingresso");
            var sessions = ServerContext.Current.Sessions;
            PlayerSession s = null;
            yield return ctx.WaitUntil(() => sessions.TryGetByPlayerId(joins.FirstPlayerId, out s) && s.Save.Cash == 4242 &&
                                             s.Save.Inventory.ContainsKey(TestItems.Rock) && !s.Dirty, 30f, "dati salvati");
            ctx.Check("soldi e oggetti salvati (ritardo di 2 s)", ctx.LastWaitOk);
            if (s == null) yield break;
            // Modifica non importante: niente salvataggio a 2 s; il periodico è tra 60 s. Solo ServerStopping la salva.
            s.Save.SetModule(ModuleKey, "saved-at-stop");
            s.MarkDirty(important: false);
            ctx.Check("modifica in attesa del salvataggio di chiusura", s.Dirty);
        }
    }

    [AutoTestScenario("restart.after")]
    public sealed class RestartAfterScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var joins = new PersistenceTest.JoinCounter();
            yield return ctx.WaitUntil(() => joins.Joins >= 1, 90f, "ingresso dopo il riavvio");
            ctx.Check("il giocatore rientra nel server riavviato", ctx.LastWaitOk);
            string pid = joins.FirstPlayerId;
            ServerContext.Current.Sessions.TryGetByPlayerId(pid, out var s);
            ctx.Check("profilo esistente (non nuovo)", s != null && !s.IsNewAccount);
            ctx.Check("soldi dopo il riavvio = 4242", s != null && s.Save.Cash == 4242, $"soldi {s?.Save.Cash}");
            ctx.Check("dato salvato alla chiusura (ServerStopping)",
                s != null && s.Save.GetModule<string>(RestartBeforeScenario.ModuleKey) == "saved-at-stop");
            var rows = PersistenceTest.JournalFor(pid);
            bool hasStart = false, hasSet = false;
            foreach (var r in rows)
            {
                if (r.Reason == "account.start") hasStart = true;
                if (r.Reason == "test.set_cash" && r.Balance == 4242) hasSet = true;
            }
            ctx.Check("righe del registro presenti dopo il riavvio", hasStart && hasSet, PersistenceTest.Describe(rows));
            yield return PersistenceTest.WaitAllLeft(ctx, 60f);
            PersistenceTest.CheckNoSaveErrors(ctx);
        }
    }

    // =====================================================================
    // lock_two_servers: due server sulla stessa cartella dati; il giocatore può stare su uno solo.
    // =====================================================================

    [AutoTestScenario("lock.holder")]
    public sealed class LockHolderScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return PersistenceTest.WaitReady(ctx);
            if (!ctx.LastWaitOk) yield break;
            yield return PersistenceTest.Hook(ctx, "set_cash", "", long.Parse(ctx.Arg("set-cash", "3131")));
            ctx.Check("soldi impostati sul primo server", ctx.LastWaitOk && PlayerTestHooks.Local.LastOk);
            yield return ctx.WaitForRelease(90f);
            ctx.Check("resto in gioco sul primo server fino al rilascio", ctx.LastWaitOk && PlayerNetwork.Local != null);
        }
    }

    /// <summary>Client che deve essere rifiutato con un motivo che il launcher riprova (es. already_connected): poi annulla.</summary>
    [AutoTestScenario("expect_retry_reject")]
    public sealed class ExpectRetryRejectScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            string expected = ctx.Arg("expect-reason", DisconnectReasons.AlreadyConnectedCode);
            var launcher = ctx.Launcher;
            if (!ctx.Check("launcher presente", launcher != null)) yield break;
            yield return ctx.WaitUntil(() => launcher.LastAttemptReasonCode == expected || PlayerNetwork.Local != null, 40f, "rifiuto");
            ctx.Check($"rifiutato con '{expected}'", launcher.LastAttemptReasonCode == expected, $"codice {launcher.LastAttemptReasonCode ?? "nessuno"}");
            ctx.Check("mai entrato in gioco", PlayerNetwork.Local == null && launcher.State != LauncherState.InGame);
            launcher.Disconnect();
        }
    }

    [AutoTestScenario("lock.first_server")]
    public sealed class LockFirstServerScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var joins = new PersistenceTest.JoinCounter();
            yield return ctx.WaitUntil(() => joins.Joins >= 1, 60f, "ingresso");
            string pid = joins.FirstPlayerId;
            var locks = ServerContext.Current.Locks;
            ctx.Check("questo server tiene il lucchetto", locks.IsHeldByMe(pid));
            yield return PersistenceTest.WaitAllLeft(ctx, 120f);
            ctx.Check("lucchetto liberato all'uscita", !locks.IsHeldByMe(pid));
            PersistenceTest.CheckNoSaveErrors(ctx);
        }
    }

    [AutoTestScenario("lock.second_server")]
    public sealed class LockSecondServerScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            var joins = new PersistenceTest.JoinCounter();
            var server = ctx.Server;
            yield return ctx.WaitUntil(() => server.RejectionCount(DisconnectReasons.AlreadyConnectedCode) >= 1, 60f, "rifiuto already_connected");
            ctx.Check("rifiutato mentre è vivo sull'altro server", ctx.LastWaitOk && joins.Joins == 0);
            yield return ctx.WaitUntil(() => joins.Joins >= 1, 90f, "ingresso dopo l'uscita dall'altro server");
            ctx.Check("accettato dopo l'uscita dall'altro server", ctx.LastWaitOk);
            ServerContext.Current.Sessions.TryGetByPlayerId(joins.FirstPlayerId, out var s);
            ctx.Check("stesso profilo (dati del primo server)", s != null && !s.IsNewAccount && s.Save.Cash == long.Parse(ctx.Arg("expect-cash", "3131")),
                $"soldi {s?.Save.Cash}");
            yield return PersistenceTest.WaitAllLeft(ctx, 60f);
            PersistenceTest.CheckNoSaveErrors(ctx);
        }
    }

    // =====================================================================
    // testhooks_disabled: un server normale (senza -testmode) rifiuta i comandi di test.
    // =====================================================================

    [AutoTestScenario("hooks.disabled_client")]
    public sealed class HooksDisabledClientScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            yield return PersistenceTest.WaitReady(ctx);
            if (!ctx.LastWaitOk) yield break;
            yield return PersistenceTest.Hook(ctx, "set_cash", "", 999_999);
            ctx.Check("set_cash rifiutato", ctx.LastWaitOk && !PlayerTestHooks.Local.LastOk && PlayerTestHooks.Local.LastResult == "disabled",
                PlayerTestHooks.Local.LastResult);
            yield return PersistenceTest.Hook(ctx, "give_item", "test.rock", 1);
            ctx.Check("give_item rifiutato", ctx.LastWaitOk && !PlayerTestHooks.Local.LastOk, PlayerTestHooks.Local.LastResult);
            yield return PersistenceTest.Hook(ctx, "state", "", 0);
            ctx.Check("state rifiutato", ctx.LastWaitOk && !PlayerTestHooks.Local.LastOk, PlayerTestHooks.Local.LastResult);

            // Un uso di un oggetto che non ha: rifiutato dal server.
            bool? lastOk = null;
            PlayerInventory.LocalUseResult += (ok, id, msg) => lastOk = ok;
            PlayerInventory.Local.RequestUse("test.rock");
            yield return ctx.WaitUntil(() => lastOk.HasValue, 10f, "risposta all'uso");
            ctx.Check("uso di un oggetto non posseduto rifiutato", lastOk == false);

            yield return ctx.Wait(2f);
            yield return PersistenceTest.CheckExpected(ctx); // $5,000 e inventario vuoto
        }
    }

    [AutoTestScenario("hooks.disabled_server")]
    public sealed class HooksDisabledServerScenario : IAutoTestScenario
    {
        public IEnumerator Run(AutoTestContext ctx)
        {
            ctx.Check("server senza -testmode", !ctx.Args.TestMode);
            ctx.Check("comandi di test spenti", !ServerTestHooks.Enabled);
            var joins = new PersistenceTest.JoinCounter();
            yield return ctx.WaitUntil(() => joins.Joins >= 1, 60f, "ingresso");
            yield return ctx.WaitUntil(() => ServerTestHooks.RejectedCount >= 3, 30f, "tre comandi rifiutati");
            ctx.Check("tre comandi di test rifiutati", ctx.LastWaitOk, $"rifiutati {ServerTestHooks.RejectedCount}");
            ctx.Check("nessun comando eseguito", ServerTestHooks.ExecutedCount == 0);
            ctx.Check("nessun oggetto di test nel catalogo", !ItemCatalog.Contains(TestItems.Rock));
            ServerContext.Current.Sessions.TryGetByPlayerId(joins.FirstPlayerId, out var s);
            ctx.Check("soldi invariati ($5,000) e inventario vuoto", s != null && s.Save.Cash == Money.StartingCash && s.Save.Inventory.Count == 0,
                $"soldi {s?.Save.Cash}");
            yield return PersistenceTest.WaitAllLeft(ctx, 60f);
            PersistenceTest.CheckNoSaveErrors(ctx);
        }
    }
}
