using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Underworld.Net;

namespace Underworld.Tests
{
    /// <summary>Payload di connessione e regole di approvazione (logica pura, senza Netcode).</summary>
    public class NetApprovalTests
    {
        static string Token() => Convert.ToBase64String(new byte[32]);

        static ConnectionPayload Good(string name = "Alfa") => new ConnectionPayload
        {
            Version = NetConstants.ProtocolVersion,
            PlayerId = Guid.NewGuid().ToString(),
            Token = Token(),
            Name = name,
        };

        static ApprovalContext Ctx(int current = 0, int max = 16, Func<string, bool> inUse = null,
            IReadOnlyList<IConnectionValidator> validators = null) => new ApprovalContext
        {
            CurrentPlayers = current,
            MaxPlayers = max,
            IsPlayerIdInUse = inUse,
            Validators = validators,
        };

        static string Code(ApprovalResult r)
        {
            Assert.IsFalse(r.Approved, "doveva essere rifiutato");
            Assert.IsTrue(DisconnectReasons.TryParse(r.Reason, out string code, out string text), r.Reason);
            Assert.IsFalse(string.IsNullOrEmpty(text));
            return code;
        }

        [Test]
        public void Payload_RoundTrip_KeepsFields()
        {
            var p = Good();
            p.Ticket = "abc";
            var back = ConnectionPayload.TryParse(p.ToBytes());
            Assert.NotNull(back);
            Assert.AreEqual(p.Version, back.Version);
            Assert.AreEqual(p.PlayerId, back.PlayerId);
            Assert.AreEqual(p.Token, back.Token);
            Assert.AreEqual(p.Name, back.Name);
            Assert.AreEqual("abc", back.Ticket);
            StringAssert.Contains("\"v\":1", p.ToJson());
            StringAssert.Contains("\"ticket\":\"abc\"", p.ToJson());
        }

        [TestCase("")]
        [TestCase("non json")]
        [TestCase("[1,2,3]")]
        [TestCase("{\"v\": \"uno\"}")]
        [TestCase("{\"v\":1,")]
        public void Payload_Malformed_ReturnsNullWithoutThrowing(string text)
        {
            Assert.IsNull(ConnectionPayload.TryParse(Encoding.UTF8.GetBytes(text)));
        }

        [Test]
        public void Payload_Null_ReturnsNull() => Assert.IsNull(ConnectionPayload.TryParse(null));

        [Test]
        public void Token_MustBe32BytesBase64()
        {
            Assert.IsTrue(ConnectionPayload.IsValidToken(Token()));
            Assert.IsFalse(ConnectionPayload.IsValidToken(Convert.ToBase64String(new byte[16])));
            Assert.IsFalse(ConnectionPayload.IsValidToken("!!!non base64!!!"));
            Assert.IsFalse(ConnectionPayload.IsValidToken(null));
        }

        [Test]
        public void Approve_GoodPayload_Accepted()
        {
            var p = Good("  Alfa   Beta ");
            var r = ConnectionApprovalRules.Evaluate(p.ToBytes(), 1, Ctx());
            Assert.IsTrue(r.Approved, r.Reason);
            Assert.AreEqual("Alfa Beta", r.Name);
            Assert.AreEqual(Guid.Parse(p.PlayerId).ToString("D"), r.PlayerId);
        }

        [Test]
        public void Approve_TooLarge_Rejected()
        {
            var bytes = new byte[NetConstants.MaxPayloadBytes + 1];
            Assert.AreEqual(DisconnectReasons.PayloadTooLargeCode, Code(ConnectionApprovalRules.Evaluate(bytes, 1, Ctx())));
        }

        [Test]
        public void Approve_Malformed_Rejected()
        {
            var r = ConnectionApprovalRules.Evaluate(Encoding.UTF8.GetBytes("{rotto"), 1, Ctx());
            Assert.AreEqual(DisconnectReasons.PayloadInvalidCode, Code(r));
            Assert.AreEqual(DisconnectReasons.PayloadInvalidCode, Code(ConnectionApprovalRules.Evaluate(null, 1, Ctx())));
        }

        [Test]
        public void Approve_WrongVersion_Rejected()
        {
            var p = Good();
            p.Version = 99;
            Assert.AreEqual(DisconnectReasons.ProtocolVersionCode, Code(ConnectionApprovalRules.Evaluate(p.ToBytes(), 1, Ctx())));
        }

        [Test]
        public void Approve_BadIdentity_Rejected()
        {
            var p = Good();
            p.PlayerId = "non-un-guid";
            Assert.AreEqual(DisconnectReasons.IdentityInvalidCode, Code(ConnectionApprovalRules.Evaluate(p.ToBytes(), 1, Ctx())));
            p = Good();
            p.Token = "corto";
            Assert.AreEqual(DisconnectReasons.IdentityInvalidCode, Code(ConnectionApprovalRules.Evaluate(p.ToBytes(), 1, Ctx())));
        }

        [Test]
        public void Approve_BadName_RejectedWithItalianText()
        {
            var r = ConnectionApprovalRules.Evaluate(Good("x").ToBytes(), 1, Ctx());
            Assert.AreEqual(DisconnectReasons.NameInvalidCode, Code(r));
            DisconnectReasons.TryParse(r.Reason, out _, out string text);
            StringAssert.StartsWith("Nome non valido", text);
        }

        [Test]
        public void Approve_ServerFull_Rejected()
        {
            var r = ConnectionApprovalRules.Evaluate(Good().ToBytes(), 3, Ctx(current: 2, max: 2));
            Assert.AreEqual(DisconnectReasons.ServerFullCode, Code(r));
            DisconnectReasons.TryParse(r.Reason, out _, out string text);
            StringAssert.StartsWith("Server pieno", text);
        }

        [Test]
        public void Approve_SamePlayerIdConnected_Rejected()
        {
            var r = ConnectionApprovalRules.Evaluate(Good().ToBytes(), 1, Ctx(inUse: id => true));
            Assert.AreEqual(DisconnectReasons.AlreadyConnectedCode, Code(r));
        }

        sealed class Banned : IConnectionValidator
        {
            public string Validate(in ConnectionCandidate c) =>
                c.Name == "Cattivo" ? DisconnectReasons.Format("banned", "Sei stato bandito.") : null;
        }

        [Test]
        public void Approve_ExtraValidator_IsConsulted()
        {
            var validators = new List<IConnectionValidator> { new Banned() };
            Assert.AreEqual("banned", Code(ConnectionApprovalRules.Evaluate(Good("Cattivo").ToBytes(), 1, Ctx(validators: validators))));
            Assert.IsTrue(ConnectionApprovalRules.Evaluate(Good("Buono").ToBytes(), 1, Ctx(validators: validators)).Approved);
        }

        [Test]
        public void DisconnectReasons_ParseRejectsForeignText()
        {
            Assert.IsTrue(DisconnectReasons.TryParse(DisconnectReasons.ServerClosing, out string code, out string text));
            Assert.AreEqual(DisconnectReasons.ServerClosingCode, code);
            Assert.IsFalse(string.IsNullOrEmpty(text));
            Assert.IsFalse(DisconnectReasons.TryParse("[Disconnect Event] testo di Netcode", out _, out _));
            Assert.IsFalse(DisconnectReasons.TryParse("Codice Maiuscolo|testo", out _, out _));
            Assert.IsFalse(DisconnectReasons.TryParse(null, out _, out _));
        }
    }
}
