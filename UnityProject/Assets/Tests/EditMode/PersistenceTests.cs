using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Underworld.Economy;
using Underworld.Server;
using UnityEngine;
using UnityEngine.TestTools;

namespace Underworld.Tests
{
    /// <summary>Salvataggi JSON (Fase 5): scrittura atomica, .bak, quarantena, percorsi, dati dei moduli, lucchetti.</summary>
    public sealed class PersistenceTests
    {
        string _dir;
        JsonFilePlayerStore _store;

        static readonly string Token = Convert.ToBase64String(new byte[32]);

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "uw-tests-" + Guid.NewGuid().ToString("N"));
            _store = new JsonFilePlayerStore(_dir, Money.MaxCash);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        static string NewId() => Guid.NewGuid().ToString("D");

        static PlayerSave Make(string id, long cash = 100)
        {
            var s = PlayerSave.CreateNew(id, "Tester", IdentityHash.Compute(id, Token), DateTime.UtcNow);
            s.Cash = cash;
            return s;
        }

        // ---------------- Percorsi ----------------

        [Test]
        public void PlayerIdPath_AcceptsOnlyCanonicalGuid()
        {
            Assert.IsTrue(PlayerIdPath.IsValid("0f8fad5b-d9cb-469f-a165-70867728950e"));
            Assert.IsFalse(PlayerIdPath.IsValid("0F8FAD5B-D9CB-469F-A165-70867728950E"), "maiuscole: due nomi per lo stesso id");
            Assert.IsFalse(PlayerIdPath.IsValid("{0f8fad5b-d9cb-469f-a165-70867728950e}"));
            Assert.IsFalse(PlayerIdPath.IsValid("0f8fad5bd9cb469fa16570867728950e"));
            Assert.IsFalse(PlayerIdPath.IsValid("../../etc/passwd"));
            Assert.IsFalse(PlayerIdPath.IsValid("..\\0f8fad5b-d9cb-469f-a165-70867728950"));
            Assert.IsFalse(PlayerIdPath.IsValid("0f8fad5b-d9cb-469f-a165-70867728950e/../x"));
            Assert.IsFalse(PlayerIdPath.IsValid(""));
            Assert.IsFalse(PlayerIdPath.IsValid(null));
            Assert.Throws<ArgumentException>(() => PlayerIdPath.Combine(_dir, "../evil", ".json"));
            Assert.Throws<ArgumentException>(() => _store.PathFor("CON"));
        }

        [Test]
        public void Load_InvalidId_IsErrorAndTouchesNothing()
        {
            var r = _store.Load("../../x");
            Assert.AreEqual(PlayerLoadStatus.Error, r.Status);
            Assert.AreEqual(0, Directory.GetFiles(_store.PlayersDir).Length);
        }

        // ---------------- Scrittura e lettura ----------------

        [Test]
        public void SaveLoad_RoundTrip_KeepsAllFields()
        {
            string id = NewId();
            var s = Make(id, 12345);
            s.Inventory["test.snack"] = 3;
            s.LastArea = "Downtown";
            s.LastPosition = new SavedVector(1.5f, 2f, -3.25f);
            _store.Save(s);

            var r = _store.Load(id);
            Assert.AreEqual(PlayerLoadStatus.Ok, r.Status, r.Detail);
            Assert.AreEqual(12345, r.Save.Cash);
            Assert.AreEqual(3, r.Save.Inventory["test.snack"]);
            Assert.AreEqual("Downtown", r.Save.LastArea);
            Assert.AreEqual(-3.25f, r.Save.LastPosition.Z);
            Assert.AreEqual(s.TokenHash, r.Save.TokenHash);
            Assert.AreEqual(PlayerSave.CurrentSchemaVersion, r.Save.SchemaVersion);
            Assert.AreEqual(DateTimeKind.Utc, r.Save.CreatedUtc.Kind);
        }

        [Test]
        public void Load_Missing_IsNotFound()
        {
            Assert.AreEqual(PlayerLoadStatus.NotFound, _store.Load(NewId()).Status);
        }

        [Test]
        public void Save_IsAtomic_KeepsPreviousVersionAsBak_NoTmpLeft()
        {
            string id = NewId();
            _store.Save(Make(id, 1));
            Assert.IsFalse(File.Exists(_store.BackupPathFor(id)), "al primo salvataggio non c'è versione precedente");
            _store.Save(Make(id, 2));
            _store.Save(Make(id, 3));

            Assert.IsTrue(File.Exists(_store.BackupPathFor(id)));
            Assert.IsFalse(File.Exists(_store.PathFor(id) + ".tmp"));
            var bak = JObject.Parse(File.ReadAllText(_store.BackupPathFor(id)));
            Assert.AreEqual(2, (long)bak["cash"], ".bak = versione precedente");
            Assert.AreEqual(3, _store.Load(id).Save.Cash);
        }

        [Test]
        public void Load_CorruptMain_RecoversFromBak_AndQuarantinesMain()
        {
            string id = NewId();
            _store.Save(Make(id, 10));
            _store.Save(Make(id, 20));
            File.WriteAllText(_store.PathFor(id), "{ \"schemaVersion\": 1, \"playerId\": "); // scrittura troncata

            var r = _store.Load(id);
            Assert.AreEqual(PlayerLoadStatus.RecoveredFromBackup, r.Status);
            Assert.AreEqual(10, r.Save.Cash, "valori della copia .bak");
            Assert.IsTrue(_store.IsQuarantined(id), "il file rovinato è in players/corrupt");
            Assert.IsTrue(File.Exists(_store.PathFor(id)), "la copia buona torna file principale");
            Assert.AreEqual(PlayerLoadStatus.Ok, _store.Load(id).Status);
        }

        [Test]
        public void Load_BothCorrupt_QuarantinesAndRejects_NeverRecreates()
        {
            string id = NewId();
            _store.Save(Make(id, 10));
            _store.Save(Make(id, 20));
            File.WriteAllText(_store.PathFor(id), "not json");
            File.WriteAllText(_store.BackupPathFor(id), "{}");

            var r = _store.Load(id);
            Assert.AreEqual(PlayerLoadStatus.Corrupt, r.Status);
            Assert.IsNull(r.Save);
            Assert.IsFalse(File.Exists(_store.PathFor(id)));
            Assert.IsFalse(File.Exists(_store.BackupPathFor(id)));
            Assert.AreEqual(2, Directory.GetFiles(_store.CorruptDir, id + ".*").Length, "file e copia in quarantena");

            // Al prossimo ingresso resta bloccato: niente profilo nuovo vuoto al posto dei dati.
            Assert.AreEqual(PlayerLoadStatus.Corrupt, _store.Load(id).Status);
        }

        [Test]
        public void Load_WrongPlayerIdInsideFile_IsCorrupt()
        {
            string id = NewId();
            var other = Make(NewId());
            File.WriteAllText(_store.PathFor(id), other.ToJson());
            Assert.AreEqual(PlayerLoadStatus.Corrupt, _store.Load(id).Status);
        }

        [Test]
        public void Load_NegativeCash_IsCorrupt()
        {
            string id = NewId();
            var s = Make(id);
            s.Cash = -5;
            File.WriteAllText(_store.PathFor(id), s.ToJson());
            Assert.AreEqual(PlayerLoadStatus.Corrupt, _store.Load(id).Status);
        }

        [Test]
        public void Load_NewerSchema_IsErrorWithoutQuarantine()
        {
            string id = NewId();
            var json = JObject.Parse(Make(id).ToJson());
            json["schemaVersion"] = PlayerSave.CurrentSchemaVersion + 1;
            File.WriteAllText(_store.PathFor(id), json.ToString());
            Assert.AreEqual(PlayerLoadStatus.Error, _store.Load(id).Status);
            Assert.IsFalse(_store.IsQuarantined(id), "un salvataggio più nuovo non è rovinato");
            Assert.IsTrue(File.Exists(_store.PathFor(id)));
        }

        [Test]
        public void Migrations_MissingVersion_IsInvalid()
        {
            var root = new JObject { ["playerId"] = NewId() };
            Assert.AreEqual(PlayerSaveMigrations.Outcome.Invalid, PlayerSaveMigrations.Migrate(root, out _));
            root["schemaVersion"] = 1;
            Assert.AreEqual(PlayerSaveMigrations.Outcome.Ok, PlayerSaveMigrations.Migrate(root, out _));
        }

        // ---------------- Dati dei moduli ----------------

        sealed class GarageData
        {
            public List<string> Cars = new List<string>();
            public int Slots;
            public DateTime BoughtUtc;
        }

        [Test]
        public void Modules_GetSet_RoundTripThroughDisk()
        {
            string id = NewId();
            var s = Make(id);
            var when = new DateTime(2026, 11, 27, 10, 30, 0, DateTimeKind.Utc);
            s.SetModule("vehicles.garage", new GarageData { Cars = { "car.sedan", "car.van" }, Slots = 4, BoughtUtc = when });
            s.SetModule("missions.stats", 7);
            _store.Save(s);

            var loaded = _store.Load(id).Save;
            var g = loaded.GetModule<GarageData>("vehicles.garage");
            CollectionAssert.AreEqual(new[] { "car.sedan", "car.van" }, g.Cars);
            Assert.AreEqual(4, g.Slots);
            Assert.AreEqual(when, g.BoughtUtc);
            Assert.AreEqual(7, loaded.GetModule<int>("missions.stats"));
            Assert.IsNull(loaded.GetModule<GarageData>("missing.key"));
            Assert.IsFalse(loaded.TryGetModule<GarageData>("missions.stats", out _), "un int non è un GarageData");
            Assert.Throws<ArgumentException>(() => loaded.SetModule("Bad Key!", 1));
            Assert.IsTrue(loaded.RemoveModule("missions.stats"));
            Assert.IsFalse(loaded.HasModule("missions.stats"));
        }

        [Test]
        public void Modules_StringThatLooksLikeDate_StaysString()
        {
            string id = NewId();
            var s = Make(id);
            s.SetModule("social.note", "2026-11-27T10:00:00Z");
            _store.Save(s);
            Assert.AreEqual("2026-11-27T10:00:00Z", _store.Load(id).Save.GetModule<string>("social.note"));
        }

        // ---------------- Lucchetti ----------------

        [Test]
        public void LockRules_OwnStaleHeld()
        {
            var now = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
            var info = new SessionLockInfo { Pid = 10, Port = 7777, HeartbeatUtc = now.AddSeconds(-5).ToString("o") };
            Assert.AreEqual(LockDecision.Own, SessionLockRules.Decide(info, now, 10, 7777, now));
            Assert.AreEqual(LockDecision.HeldByOther, SessionLockRules.Decide(info, now, 11, 7777, now));
            Assert.AreEqual(LockDecision.HeldByOther, SessionLockRules.Decide(info, now, 10, 7778, now), "stesso pid, altra porta = altro server");
            info.HeartbeatUtc = now.AddSeconds(-16).ToString("o");
            Assert.AreEqual(LockDecision.Stale, SessionLockRules.Decide(info, now, 11, 7777, now));
            Assert.AreEqual(LockDecision.Stale, SessionLockRules.Decide(null, now.AddSeconds(-20), 11, 7777, now), "file illeggibile e vecchio");
            Assert.AreEqual(LockDecision.HeldByOther, SessionLockRules.Decide(null, now.AddSeconds(-2), 11, 7777, now), "file illeggibile e recente");
        }

        [Test]
        public void LockStore_TwoServers_LiveLockBlocks_StaleLockCanBeTaken()
        {
            var now = DateTime.UtcNow;
            var a = new SessionLockStore(_dir, "Downtown", 100, 7777, () => now);
            var b = new SessionLockStore(_dir, "Industrial", 200, 7778, () => now);
            string id = NewId();

            Assert.IsTrue(a.TryAcquire(id, out _));
            Assert.IsTrue(a.TryAcquire(id, out _), "lo stesso server lo riprende");
            Assert.IsFalse(b.TryAcquire(id, out var holder));
            Assert.AreEqual(100, holder.Pid);
            Assert.AreEqual("Downtown", holder.Area);

            b.Release(id); // non è suo: non deve toglierlo
            Assert.IsTrue(a.IsHeldByMe(id));

            now = now.AddSeconds(20); // A smette di battere
            var b2 = new SessionLockStore(_dir, "Industrial", 200, 7778, () => now);
            Assert.IsTrue(b2.TryAcquire(id, out _), "lucchetto scaduto");
            Assert.IsFalse(a.Heartbeat(id), "A ha perso il lucchetto");
            b2.Release(id);
            Assert.IsFalse(File.Exists(b2.PathFor(id)));
        }
    }
}
