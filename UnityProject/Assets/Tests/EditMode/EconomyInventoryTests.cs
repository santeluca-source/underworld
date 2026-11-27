using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Underworld.Economy;
using Underworld.Inventory;
using Underworld.Net;
using Underworld.Server;
using UnityEngine;
using UnityEngine.TestTools;

namespace Underworld.Tests
{
    /// <summary>Identità (hash e confronto), economia (Money, EconomyService, registro) e inventario (Fasi 5–6).</summary>
    public sealed class EconomyInventoryTests
    {
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "uw-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ItemCatalog.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ItemCatalog.ClearForTests();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        static PlayerSession MakeSession(long cash = 0)
        {
            string id = Guid.NewGuid().ToString("D");
            var entry = new ServerPlayerEntry(1, id, "Tester", null);
            var save = PlayerSave.CreateNew(id, "Tester", IdentityHash.Compute(id, "tok"), DateTime.UtcNow);
            save.Cash = cash;
            return new PlayerSession(entry, save, false);
        }

        // ---------------- Identità ----------------

        [Test]
        public void IdentityHash_IsSha256HexOfIdColonToken()
        {
            Assert.AreEqual(IdentityHash.HexLength, IdentityHash.Compute("a", "b").Length);
            string h = IdentityHash.Compute("0f8fad5b-d9cb-469f-a165-70867728950e", "dG9rZW4=");
            Assert.IsTrue(IdentityHash.IsValidHash(h));
            Assert.AreEqual(h, IdentityHash.Compute("0f8fad5b-d9cb-469f-a165-70867728950e", "dG9rZW4="), "deterministico");
            Assert.AreNotEqual(h, IdentityHash.Compute("0f8fad5b-d9cb-469f-a165-70867728950e", "dG9rZW5="));
            Assert.AreNotEqual(h, IdentityHash.Compute("1f8fad5b-d9cb-469f-a165-70867728950e", "dG9rZW4="), "l'id fa parte dell'hash");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var expected = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("x:y"))).Replace("-", "").ToLowerInvariant();
                Assert.AreEqual(expected, IdentityHash.Compute("x", "y"));
            }
        }

        [Test]
        public void IdentityHash_FixedTimeEquals_AndVerify()
        {
            string id = Guid.NewGuid().ToString("D");
            string h = IdentityHash.Compute(id, "secret");
            Assert.IsTrue(IdentityHash.FixedTimeEquals(h, string.Copy(h)));
            Assert.IsFalse(IdentityHash.FixedTimeEquals(h, h.Substring(0, 63) + (h[63] == 'a' ? 'b' : 'a')), "ultimo carattere diverso");
            Assert.IsFalse(IdentityHash.FixedTimeEquals(h, h.Substring(1)), "lunghezza diversa");
            Assert.IsFalse(IdentityHash.FixedTimeEquals(null, h));
            Assert.IsTrue(IdentityHash.Verify(id, "secret", h));
            Assert.IsFalse(IdentityHash.Verify(id, "Secret", h));
            Assert.IsFalse(IdentityHash.Verify(id, "secret", "not-a-hash"));
            Assert.IsFalse(IdentityHash.IsValidHash(h.ToUpperInvariant()));
        }

        // ---------------- Money ----------------

        [Test]
        public void Money_CreditAndDebit()
        {
            Assert.AreEqual(MoneyError.None, Money.TryCredit(100, 50, out long b));
            Assert.AreEqual(150, b);
            Assert.AreEqual(MoneyError.None, Money.TryDebit(150, 150, out b));
            Assert.AreEqual(0, b);
            Assert.AreEqual(MoneyError.InsufficientFunds, Money.TryDebit(10, 11, out b));
            Assert.AreEqual(10, b, "saldo invariato");
        }

        [Test]
        public void Money_RejectsZeroAndNegative()
        {
            Assert.AreEqual(MoneyError.InvalidAmount, Money.TryCredit(100, 0, out _));
            Assert.AreEqual(MoneyError.InvalidAmount, Money.TryCredit(100, -1, out _));
            Assert.AreEqual(MoneyError.InvalidAmount, Money.TryDebit(100, 0, out _));
            Assert.AreEqual(MoneyError.InvalidAmount, Money.TryDebit(100, -50, out long b), "un addebito negativo non deve diventare un accredito");
            Assert.AreEqual(100, b);
            Assert.AreEqual(MoneyError.InvalidAmount, Money.TryDebit(100, long.MinValue, out _));
        }

        [Test]
        public void Money_OverflowAndCap()
        {
            Assert.AreEqual(MoneyError.CapExceeded, Money.TryCredit(Money.MaxCash, 1, out long b));
            Assert.AreEqual(Money.MaxCash, b);
            Assert.AreEqual(MoneyError.CapExceeded, Money.TryCredit(1, long.MaxValue, out _), "overflow del long");
            Assert.AreEqual(MoneyError.None, Money.TryCredit(Money.MaxCash - 1, 1, out b));
            Assert.AreEqual(Money.MaxCash, b);
            Assert.AreEqual(MoneyError.InvalidBalance, Money.TryCredit(-1, 5, out _));
            Assert.AreEqual(MoneyError.InvalidBalance, Money.TryDebit(Money.MaxCash + 1, 5, out _));
            Assert.AreEqual(1_000_000_000_000L, Money.MaxCash);
            Assert.AreEqual(5_000L, Money.StartingCash);
        }

        // ---------------- Registro ----------------

        [Test]
        public void Journal_LineFormat_IsOneJsonObject()
        {
            var utc = new DateTime(2026, 11, 27, 9, 5, 3, 250, DateTimeKind.Utc);
            string line = TransactionJournal.FormatLine(utc, "Downtown", "0f8fad5b-d9cb-469f-a165-70867728950e", "Pia", -300, 4700, "shop.burger");
            Assert.AreEqual("{\"utc\":\"2026-11-27T09:05:03.250Z\",\"area\":\"Downtown\",\"playerId\":\"0f8fad5b-d9cb-469f-a165-70867728950e\"," +
                            "\"name\":\"Pia\",\"delta\":-300,\"balance\":4700,\"reason\":\"shop.burger\"}", line);
            Assert.IsFalse(line.Contains("\n"));
            var back = TransactionJournal.ParseLine(line);
            Assert.AreEqual(-300, back.Delta);
            Assert.AreEqual(4700, back.Balance);
            Assert.AreEqual("2026-11-27.jsonl", TransactionJournal.FileNameFor(utc));
            // Un nome con virgolette o a capo resta su una riga sola.
            Assert.IsFalse(TransactionJournal.FormatLine(utc, "A", "p", "x\"\ny", 1, 1, "r").Contains("\n"));
        }

        [Test]
        public void EconomyService_CreditDebit_WritesJournalAndMarksDirty()
        {
            var journal = new TransactionJournal(_dir, "TestGround");
            var eco = new EconomyService(journal);
            var s = MakeSession(1000);

            Assert.IsTrue(eco.Credit(s, 250, "test.reward"));
            Assert.AreEqual(1250, eco.GetCash(s));
            Assert.IsTrue(s.Dirty, "una modifica dei soldi va salvata");
            Assert.IsTrue(eco.TryDebit(s, 1000, "test.buy"));
            Assert.AreEqual(250, eco.GetCash(s));

            Assert.IsFalse(eco.TryDebit(s, 251, "test.buy"), "soldi insufficienti");
            Assert.AreEqual(250, eco.GetCash(s));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("rifiutato"));
            Assert.IsFalse(eco.Credit(s, -10, "test.hack"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("rifiutato"));
            Assert.IsFalse(eco.TryDebit(s, 0, "test.hack"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("rifiutato"));
            Assert.IsFalse(eco.Credit(s, Money.MaxCash, "test.cap"));
            Assert.AreEqual(250, eco.GetCash(s));

            string[] lines = File.ReadAllLines(Path.Combine(journal.Dir, TransactionJournal.FileNameFor(DateTime.UtcNow)));
            Assert.AreEqual(2, lines.Length, "solo i movimenti riusciti");
            var first = TransactionJournal.ParseLine(lines[0]);
            Assert.AreEqual(250, first.Delta);
            Assert.AreEqual(1250, first.Balance);
            Assert.AreEqual(s.PlayerId, first.PlayerId);
            Assert.AreEqual("TestGround", first.Area);
            Assert.AreEqual(-1000, TransactionJournal.ParseLine(lines[1]).Delta);
        }

        [Test]
        public void EconomyService_ClosedSession_Refused()
        {
            var eco = new EconomyService(null);
            var s = MakeSession(100);
            s.Closed = true;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("sessione chiusa"));
            Assert.IsFalse(eco.Credit(s, 5, "late"));
            Assert.AreEqual(100, s.Save.Cash);
        }

        [Test]
        public void EconomyService_CleanReason()
        {
            Assert.AreEqual("unspecified", EconomyService.CleanReason("  "));
            Assert.AreEqual("a b", EconomyService.CleanReason("a\nb"));
            Assert.AreEqual(EconomyService.MaxReasonLength, EconomyService.CleanReason(new string('x', 200)).Length);
        }

        // ---------------- Inventario ----------------

        static ItemDefinition Def(string id, int maxStack, bool usable = false) => new ItemDefinition
        {
            Id = id, DisplayName = "Item " + id, Description = "Test.", MaxStack = maxStack, Usable = usable,
        };

        [Test]
        public void Catalog_RegisterValidatesAndRejectsDuplicates()
        {
            ItemCatalog.Register(Def("food.burger", 5));
            Assert.IsTrue(ItemCatalog.Contains("food.burger"));
            Assert.Throws<InvalidOperationException>(() => ItemCatalog.Register(Def("food.burger", 5)));
            Assert.Throws<ArgumentException>(() => ItemCatalog.Register(Def("Bad Id", 5)));
            Assert.Throws<ArgumentException>(() => ItemCatalog.Register(Def("ok.id", 0)));
            Assert.IsFalse(ItemCatalog.TryRegister(Def("food.burger", 5)));
            var shop = Def("tool.drill", 1);
            shop.SoldAt = new[] { "downtown.shop.general" };
            ItemCatalog.Register(shop);
            Assert.AreEqual(1, ItemCatalog.SoldBy("downtown.shop.general").Count);
            Assert.IsTrue(ItemCatalog.Unregister("tool.drill"));
            Assert.IsNull(ItemCatalog.Get("tool.drill"));
        }

        [Test]
        public void InventoryRules_StackingAndRemoval()
        {
            var inv = new Dictionary<string, int>();
            var snack = Def("food.snack", 5);
            Assert.AreEqual(InventoryError.None, InventoryRules.TryAdd(inv, snack, 3));
            Assert.AreEqual(InventoryError.None, InventoryRules.TryAdd(inv, snack, 2));
            Assert.AreEqual(5, InventoryRules.Count(inv, "food.snack"));
            Assert.AreEqual(InventoryError.StackFull, InventoryRules.TryAdd(inv, snack, 1));
            Assert.AreEqual(InventoryError.InvalidCount, InventoryRules.TryAdd(inv, snack, 0));
            Assert.AreEqual(InventoryError.InvalidCount, InventoryRules.TryAdd(inv, snack, -2));
            Assert.AreEqual(InventoryError.UnknownItem, InventoryRules.TryAdd(inv, null, 1));
            Assert.AreEqual(InventoryError.StackFull, InventoryRules.CanAdd(inv, snack, int.MaxValue), "niente overflow");

            Assert.AreEqual(InventoryError.NotEnough, InventoryRules.TryRemove(inv, "food.snack", 6));
            Assert.AreEqual(InventoryError.None, InventoryRules.TryRemove(inv, "food.snack", 4));
            Assert.AreEqual(1, inv["food.snack"]);
            Assert.AreEqual(InventoryError.InvalidCount, InventoryRules.TryRemove(inv, "food.snack", -1));
            Assert.AreEqual(InventoryError.None, InventoryRules.TryRemove(inv, "food.snack", 1));
            Assert.IsFalse(inv.ContainsKey("food.snack"), "a zero la riga sparisce");
            Assert.AreEqual(InventoryError.NotEnough, InventoryRules.TryRemove(inv, "nothing", 1));
        }

        [Test]
        public void InventoryRules_DistinctItemLimit()
        {
            var inv = new Dictionary<string, int>();
            for (int i = 0; i < InventoryRules.MaxDistinctItems; i++)
                Assert.AreEqual(InventoryError.None, InventoryRules.TryAdd(inv, Def("item." + i, 3), 1));
            Assert.AreEqual(InventoryError.NoSpace, InventoryRules.TryAdd(inv, Def("item.extra", 3), 1));
            Assert.AreEqual(InventoryError.None, InventoryRules.TryAdd(inv, Def("item.0", 3), 1), "una riga che c'è già si può riempire");
        }

        sealed class Handler : IItemUseHandler
        {
            public bool Result;
            public int Calls;
            public bool TryUse(PlayerSession session, ItemDefinition item, out string message)
            {
                Calls++;
                message = Result ? "ok" : "nope";
                return Result;
            }
        }

        [Test]
        public void InventoryService_UseRemovesOnlyOnSuccess()
        {
            ItemCatalog.Register(Def("food.snack", 5, usable: true));
            ItemCatalog.Register(Def("misc.rock", 5));
            var handler = new Handler();
            ItemUseHandlers.Register("food.snack", handler);
            try
            {
                var svc = new InventoryService();
                var s = MakeSession();
                Assert.IsTrue(svc.TryAdd(s, "food.snack", 2, "test"));
                Assert.IsTrue(svc.TryAdd(s, "misc.rock", 1, "test"));
                Assert.IsFalse(svc.TryAdd(s, "unknown.item", 1, "test"));
                Assert.IsTrue(s.Dirty);

                handler.Result = false;
                Assert.AreEqual(UseResult.Failed, svc.TryUse(s, "food.snack", out _));
                Assert.AreEqual(2, svc.Count(s, "food.snack"), "il gestore ha fallito: l'oggetto resta");

                handler.Result = true;
                Assert.AreEqual(UseResult.Used, svc.TryUse(s, "food.snack", out _));
                Assert.AreEqual(1, svc.Count(s, "food.snack"));
                Assert.AreEqual(UseResult.NotUsable, svc.TryUse(s, "misc.rock", out _));
                Assert.AreEqual(1, svc.Count(s, "misc.rock"));
                Assert.AreEqual(UseResult.UnknownItem, svc.TryUse(s, "unknown.item", out _));

                Assert.IsTrue(svc.TryRemove(s, "food.snack", 1, "test"));
                Assert.AreEqual(UseResult.NotOwned, svc.TryUse(s, "food.snack", out _));
                Assert.AreEqual(2, handler.Calls, "senza l'oggetto il gestore non viene chiamato");
            }
            finally
            {
                ItemUseHandlers.Unregister("food.snack");
            }
        }

        // ---------------- Approvazione con sostituzione ----------------

        [Test]
        public void Approval_SamePlayerId_ReplaceOnlyWhenPolicyAllows()
        {
            var payload = new ConnectionPayload
            {
                Version = NetConstants.ProtocolVersion,
                PlayerId = Guid.NewGuid().ToString("D"),
                Token = Convert.ToBase64String(new byte[32]),
                Name = "Pia",
            };
            var ctx = new ApprovalContext { MaxPlayers = 1, CurrentPlayers = 1, IsPlayerIdInUse = _ => true };
            Assert.AreEqual(DisconnectReasons.AlreadyConnectedCode, Code(ConnectionApprovalRules.Evaluate(payload.ToBytes(), 2, ctx)));

            ctx.AllowReplace = c => c.PlayerIdInUse;
            var r = ConnectionApprovalRules.Evaluate(payload.ToBytes(), 2, ctx);
            Assert.IsTrue(r.Approved, "chi sostituisce la sua sessione entra anche a server pieno");
            Assert.IsTrue(r.ReplacesExisting);

            ctx.IsPlayerIdInUse = _ => false;
            Assert.AreEqual(DisconnectReasons.ServerFullCode, Code(ConnectionApprovalRules.Evaluate(payload.ToBytes(), 2, ctx)));
        }

        static string Code(ApprovalResult r)
        {
            DisconnectReasons.TryParse(r.Reason, out string code, out _);
            return code;
        }
    }
}
