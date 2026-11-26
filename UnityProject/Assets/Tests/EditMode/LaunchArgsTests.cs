using NUnit.Framework;
using Underworld.Core;

namespace Underworld.Tests
{
    public class LaunchArgsTests
    {
        [Test]
        public void Parses_ServerArguments()
        {
            var a = LaunchArgs.Parse(new[] { "game.exe", "-server", "-area", "Industrial", "-port", "7801", "-datadir", "C:/tmp/x", "-testmode" });
            Assert.IsTrue(a.IsServer);
            Assert.AreEqual(AreaId.Industrial, a.Area);
            Assert.AreEqual(7801, a.Port);
            Assert.AreEqual("C:/tmp/x", a.DataDir);
            Assert.IsTrue(a.TestMode);
            Assert.AreEqual("127.0.0.1", a.ListenAddress);
        }

        [Test]
        public void Defaults_WhenMissing()
        {
            var a = LaunchArgs.Parse(new[] { "game.exe" });
            Assert.IsFalse(a.IsServer);
            Assert.AreEqual(AreaId.Downtown, a.Area);
            Assert.AreEqual(AreaInfo.DefaultPort(AreaId.Downtown), a.Port);
            Assert.IsNull(a.ConnectAddress);
            Assert.IsFalse(a.TestMode);
        }

        [Test]
        public void AreaName_IsCaseInsensitive_AndPortFollowsArea()
        {
            var a = LaunchArgs.Parse(new[] { "x", "-area", "suburbs" });
            Assert.AreEqual(AreaId.Suburbs, a.Area);
            Assert.AreEqual(7779, a.Port);
        }

        [Test]
        public void NegativeNumber_IsAValue_NotAKey()
        {
            var a = LaunchArgs.Parse(new[] { "x", "-offset", "-5", "-bot", "walk" });
            Assert.AreEqual(-5, a.GetInt("offset", 0));
            Assert.AreEqual("walk", a.Bot);
        }
    }
}
