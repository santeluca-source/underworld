using System.Collections.Generic;
using NUnit.Framework;
using Underworld.Core;
using Underworld.Net;
using UnityEngine;

namespace Underworld.Tests
{
    /// <summary>Regola del nome, limite di frequenza, scelta dello spawn, indirizzi.</summary>
    public class NetRulesTests
    {
        [TestCase("Alfa", true)]
        [TestCase("abc", true)]
        [TestCase("Giocatore_16_car", true)]
        [TestCase("Ugo-Bassi 2", true)]
        [TestCase("Àlbèrto", true)]
        [TestCase("x", false)]
        [TestCase("ab", false)]
        [TestCase("abcdefghijklmnopq", false)]
        [TestCase("nome!", false)]
        [TestCase("<script>", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void NameRule(string raw, bool expected)
        {
            Assert.AreEqual(expected, PlayerNameRules.TryNormalize(raw, out _), raw);
        }

        [Test]
        public void NameRule_NormalizesSpaces()
        {
            Assert.IsTrue(PlayerNameRules.TryNormalize("   Mario    Rossi  ", out string n));
            Assert.AreEqual("Mario Rossi", n);
            Assert.IsFalse(PlayerNameRules.TryNormalize("  a  ", out _)); // dopo la pulizia resta "a"
        }

        [Test]
        public void RateLimiter_BurstThenRefill()
        {
            double now = 0;
            var rl = new RateLimiter(2f, 3f, () => now);
            Assert.IsTrue(rl.TryConsume(1, "k"));
            Assert.IsTrue(rl.TryConsume(1, "k"));
            Assert.IsTrue(rl.TryConsume(1, "k"));
            Assert.IsFalse(rl.TryConsume(1, "k"), "il picco è 3");
            now = 0.5; // +1 gettone
            Assert.IsTrue(rl.TryConsume(1, "k"));
            Assert.IsFalse(rl.TryConsume(1, "k"));
            now = 100; // il secchio non supera il picco
            Assert.AreEqual(3f, rl.Available(1, "k"), 0.001f);
        }

        [Test]
        public void RateLimiter_SeparateBucketsPerClientAndKey()
        {
            double now = 0;
            var rl = new RateLimiter(1f, 1f, () => now);
            Assert.IsTrue(rl.TryConsume(1, "a"));
            Assert.IsFalse(rl.TryConsume(1, "a"));
            Assert.IsTrue(rl.TryConsume(1, "b"));
            Assert.IsTrue(rl.TryConsume(2, "a"));
            Assert.AreEqual(3, rl.BucketCount);
            rl.RemoveClient(1);
            Assert.AreEqual(1, rl.BucketCount);
            Assert.IsTrue(rl.TryConsume(1, "a"), "dopo RemoveClient il client riparte pieno");
        }

        [Test]
        public void SpawnSelector_PicksFreePointsFirst()
        {
            var points = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(20, 0, 0) };
            Assert.AreEqual(0, SpawnSelector.Choose(points, new List<Vector3>()));
            Assert.AreEqual(1, SpawnSelector.Choose(points, new List<Vector3> { new Vector3(0, 0, 0) }, new List<int> { 1, 0, 0 }));
            int third = SpawnSelector.Choose(points, new List<Vector3> { new Vector3(0, 0, 0), new Vector3(10, 0, 0) }, new List<int> { 1, 1, 0 });
            Assert.AreEqual(2, third);
            Assert.AreEqual(-1, SpawnSelector.Choose(new List<Vector3>(), null));
            Assert.AreEqual(Vector3.zero, SpawnSelector.CrowdOffset(0));
            Assert.Greater(SpawnSelector.CrowdOffset(3).magnitude, 1f);
        }

        [TestCase("127.0.0.1:7777", "127.0.0.1", 7777)]
        [TestCase(" 192.168.1.5:7700 ", "192.168.1.5", 7700)]
        public void Address_Parse(string text, string ip, int port)
        {
            Assert.IsTrue(GameRoot.TryParseAddress(text, out string i, out ushort p));
            Assert.AreEqual(ip, i);
            Assert.AreEqual(port, p);
        }

        [TestCase("127.0.0.1")]
        [TestCase("127.0.0.1:")]
        [TestCase(":7777")]
        [TestCase("127.0.0.1:0")]
        [TestCase("127.0.0.1:99999")]
        [TestCase(null)]
        public void Address_ParseRejects(string text)
        {
            Assert.IsFalse(GameRoot.TryParseAddress(text, out _, out _));
        }

        [Test]
        public void LaunchArgs_TestOverridesAreReadable()
        {
            var a = LaunchArgs.Parse(new[] { "-maxplayers", "2", "-localport", "7705", "-localarea", "TestGround" });
            Assert.AreEqual(2, a.GetInt("maxplayers", 16));
            Assert.AreEqual(7705, a.GetInt("localport", 0));
            Assert.AreEqual("TestGround", a.Get("localarea"));
        }

        [Test]
        public void Server_AreaWithoutWorldOrSpawns_IsNotPlayable()
        {
            // Mondo non costruito (eccezione nel costruttore) o nessun punto di spawn: il server non è pronto.
            Assert.IsFalse(ServerRuntime.IsAreaPlayable(false, 3));
            Assert.IsFalse(ServerRuntime.IsAreaPlayable(true, 0));
            Assert.IsTrue(ServerRuntime.IsAreaPlayable(true, 1));
        }

        [Test]
        public void Launcher_QuoteArg_KeepsTrailingBackslashAndQuotes()
        {
            Assert.AreEqual(@"""C:\dati""", GameLauncher.QuoteArg(@"C:\dati"));
            // Barra finale raddoppiata: altrimenti \" diventa una virgoletta letterale.
            Assert.AreEqual(@"""C:\a b\\""", GameLauncher.QuoteArg(@"C:\a b\"));
            Assert.AreEqual(@"""a\""b""", GameLauncher.QuoteArg(@"a""b"));
            Assert.AreEqual(@"""""", GameLauncher.QuoteArg(""));
            Assert.AreEqual(@"C:\dati", GameLauncher.TrimDirectory(@"C:\dati\"));
            Assert.AreEqual(@"C:\", GameLauncher.TrimDirectory(@"C:\"));
        }

        [Test]
        public void Launcher_ReasonTextFromServer_IsPlainAndShort()
        {
            Assert.AreEqual("bsize=99big text/b", GameLauncher.CleanReasonText("<b><size=99>big text</b>"));
            Assert.IsNull(GameLauncher.CleanReasonText("<>"));
            string longText = GameLauncher.CleanReasonText(new string('x', 5000));
            Assert.LessOrEqual(longText.Length, GameLauncher.MaxReasonLength + 3);
            Assert.AreEqual("a b", GameLauncher.CleanReasonText("a\nb"));
        }

        [Test]
        public void Launcher_AreaWithoutGame_GoesBackToMenu()
        {
            // Cancel durante il caricamento dell'area: l'area si attiva con il launcher fermo.
            Assert.IsTrue(GameLauncher.ShouldLeaveArea("Area_Downtown", false, LauncherState.Idle, false, true));
            Assert.IsTrue(GameLauncher.ShouldLeaveArea("Area_Downtown", false, LauncherState.Failed, false, false));
            // Connessione in corso o partita: si resta.
            Assert.IsFalse(GameLauncher.ShouldLeaveArea("Area_Downtown", false, LauncherState.Connecting, true, false));
            Assert.IsFalse(GameLauncher.ShouldLeaveArea("Area_Downtown", false, LauncherState.InGame, true, false));
            // Server e scene che non sono aree: mai.
            Assert.IsFalse(GameLauncher.ShouldLeaveArea("Area_Downtown", true, LauncherState.Idle, false, false));
            Assert.IsFalse(GameLauncher.ShouldLeaveArea("MainMenu", false, LauncherState.Idle, false, true));
        }

        [Test]
        public void Launcher_ReadsPidAndPortFromReadyFile()
        {
            Assert.IsTrue(GameLauncher.TryParseReady("{\"pid\":4321,\"area\":\"Downtown\",\"port\":7777,\"listen\":\"127.0.0.1\"}", out int pid, out int port));
            Assert.AreEqual(4321, pid);
            Assert.AreEqual(7777, port);
            Assert.IsFalse(GameLauncher.TryParseReady("{\"area\":\"Downtown\"}", out _, out _));
        }
    }
}
