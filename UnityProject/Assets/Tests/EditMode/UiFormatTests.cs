using NUnit.Framework;
using Underworld.UI;

namespace Underworld.Tests
{
    /// <summary>Formati della UI: soldi in formato italiano, nome del giocatore, indirizzo per "Unisciti".</summary>
    public class UiFormatTests
    {
        [TestCase(0L, "0")]
        [TestCase(7L, "7")]
        [TestCase(999L, "999")]
        [TestCase(1000L, "1,000")]
        [TestCase(12500L, "12,500")]
        [TestCase(1234567L, "1,234,567")]
        [TestCase(-5000L, "-5,000")]
        [TestCase(-999L, "-999")]
        [TestCase(long.MaxValue, "9,223,372,036,854,775,807")]
        [TestCase(long.MinValue, "-9,223,372,036,854,775,808")]
        public void Thousands_UsesEnglishCommas(long value, string expected)
        {
            Assert.AreEqual(expected, UiFormat.Thousands(value));
        }

        [Test]
        public void Money_PutsCurrencyBeforeNumber()
        {
            Assert.AreEqual("$12,500", UiFormat.Money(12500));
            Assert.AreEqual("$0", UiFormat.Money(0));
            Assert.AreEqual("-$300", UiFormat.Money(-300));
        }

        [Test]
        public void MoneyDelta_HasSign()
        {
            Assert.AreEqual("+$2,500", UiFormat.MoneyDelta(2500));
            Assert.AreEqual("-$300", UiFormat.MoneyDelta(-300));
            Assert.AreEqual("$0", UiFormat.MoneyDelta(0));
        }

        [TestCase("Ada", NameCheck.Ok)]
        [TestCase("  Neo_77  ", NameCheck.Ok)]
        [TestCase("Luca Rossi-2", NameCheck.Ok)]
        [TestCase("Àlvaro", NameCheck.Ok)]
        [TestCase("1234567890123456", NameCheck.Ok)]
        [TestCase("", NameCheck.Empty)]
        [TestCase("   ", NameCheck.Empty)]
        [TestCase(null, NameCheck.Empty)]
        [TestCase("Al", NameCheck.TooShort)]
        [TestCase("12345678901234567", NameCheck.TooLong)]
        [TestCase("Neo!", NameCheck.InvalidChar)]
        [TestCase("a.b.c", NameCheck.InvalidChar)]
        [TestCase("<b>x</b>", NameCheck.InvalidChar)]
        public void PlayerName_FollowsServerRules(string name, NameCheck expected)
        {
            Assert.AreEqual(expected, PlayerNameRules.Check(name));
        }

        [Test]
        public void PlayerName_MessagesHaveNoFinalPeriod()
        {
            foreach (NameCheck c in System.Enum.GetValues(typeof(NameCheck)))
            {
                string m = PlayerNameRules.Message(c);
                Assert.IsNotEmpty(m);
                Assert.IsFalse(m.EndsWith("."), m);
            }
        }

        [Test]
        public void JoinAddress_DefaultPortAndInlinePort()
        {
            Assert.IsTrue(JoinAddress.TryParse("192.168.1.20", "", out var ip, out var port, out _));
            Assert.AreEqual("192.168.1.20", ip);
            Assert.AreEqual(7777, port);

            Assert.IsTrue(JoinAddress.TryParse("10.0.0.5:7780", "7777", out ip, out port, out _));
            Assert.AreEqual("10.0.0.5", ip);
            Assert.AreEqual(7780, port);

            Assert.IsTrue(JoinAddress.TryParse("localhost", "7790", out ip, out port, out _));
            Assert.AreEqual("127.0.0.1", ip);
            Assert.AreEqual(7790, port);
        }

        [TestCase("", "7777")]
        [TestCase("256.1.1.1", "7777")]
        [TestCase("1.2.3", "7777")]
        [TestCase("server.example", "7777")]
        [TestCase("1.2.3.4", "0")]
        [TestCase("1.2.3.4", "70000")]
        [TestCase("1.2.3.4", "abc")]
        [TestCase("::1", "7777")]
        public void JoinAddress_RejectsBadInput(string address, string port)
        {
            Assert.IsFalse(JoinAddress.TryParse(address, port, out _, out _, out string error));
            Assert.IsNotEmpty(error);
        }
    }
}
