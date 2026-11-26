using NUnit.Framework;
using Underworld.Player;
using UnityEngine;

namespace Underworld.Tests
{
    /// <summary>Calcoli del controllo di movimento (MovementBudget) e modi dei bot.</summary>
    public class PlayerMovementTests
    {
        /// <summary>Simula spostamenti costanti a una certa velocità per alcuni secondi, a 30 aggiornamenti al secondo.</summary>
        static MovementViolationKind Simulate(MovementBudget b, Vector3 velocity, float seconds, ref double now)
        {
            const float dt = 1f / 30f;
            int steps = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < steps; i++)
            {
                now += dt;
                b.Advance(now);
                var k = b.Consume(velocity * dt);
                if (k != MovementViolationKind.None) return k;
            }
            return MovementViolationKind.None;
        }

        [Test]
        public void Sprint_IsAlwaysValid()
        {
            double now = 0;
            var b = new MovementBudget(MovementLimits.OnFoot);
            b.Reset(now);
            Assert.AreEqual(MovementViolationKind.None, Simulate(b, new Vector3(7.5f, 0, 0), 60f, ref now));
        }

        [Test]
        public void ThirtyMetersPerSecond_IsDetectedWithinTwoSeconds()
        {
            double now = 0;
            var b = new MovementBudget(MovementLimits.OnFoot);
            b.Reset(now);
            Assert.AreEqual(MovementViolationKind.Speed, Simulate(b, new Vector3(30f, 0, 0), 2f, ref now));
        }

        [Test]
        public void SlightlyOverLimit_IsDetectedEventually()
        {
            double now = 0;
            var b = new MovementBudget(MovementLimits.OnFoot);
            b.Reset(now);
            Assert.AreEqual(MovementViolationKind.Speed, Simulate(b, new Vector3(10f, 0, 0), 30f, ref now));
        }

        [Test]
        public void BurstyNetwork_IsTolerated()
        {
            // 1 s senza pacchetti, poi tutto lo spostamento di una corsa in un colpo.
            var b = new MovementBudget(MovementLimits.OnFoot);
            b.Reset(0);
            b.Consume(new Vector3(0.1f, 0, 0));
            b.Advance(1.0);
            Assert.AreEqual(MovementViolationKind.None, b.Consume(new Vector3(7.5f, 0, 0)));
        }

        [Test]
        public void Teleport_IsDetectedImmediately()
        {
            var b = new MovementBudget(MovementLimits.OnFoot);
            b.Reset(0);
            b.Advance(0.033);
            Assert.AreEqual(MovementViolationKind.Teleport, b.Consume(new Vector3(50f, 0, 0)));
        }

        [Test]
        public void Falls_AreAllowed_Flying_IsNot()
        {
            double now = 0;
            var fall = new MovementBudget(MovementLimits.OnFoot);
            fall.Reset(now);
            Assert.AreEqual(MovementViolationKind.None, Simulate(fall, new Vector3(0, -40f, 0), 3f, ref now));

            now = 0;
            var fly = new MovementBudget(MovementLimits.OnFoot);
            fly.Reset(now);
            Assert.AreEqual(MovementViolationKind.Vertical, Simulate(fly, new Vector3(0, 12f, 0), 3f, ref now));
        }

        [Test]
        public void Jumps_AreAllowed()
        {
            // Salto: 1,3 m in su in ~0,35 s, poi giù; ripetuto.
            var b = new MovementBudget(MovementLimits.OnFoot);
            double now = 0;
            b.Reset(now);
            for (int jump = 0; jump < 20; jump++)
            {
                for (int i = 0; i < 10; i++) { now += 0.035; b.Advance(now); Assert.AreEqual(MovementViolationKind.None, b.Consume(new Vector3(0.25f, 0.13f, 0))); }
                for (int i = 0; i < 10; i++) { now += 0.035; b.Advance(now); Assert.AreEqual(MovementViolationKind.None, b.Consume(new Vector3(0.25f, -0.13f, 0))); }
            }
        }

        [Test]
        public void RaisedLimits_AllowVehicleSpeed()
        {
            double now = 0;
            var limits = MovementLimits.OnFoot;
            limits.HorizontalSpeed = 40f;
            var b = new MovementBudget(limits);
            b.Reset(now);
            Assert.AreEqual(MovementViolationKind.None, Simulate(b, new Vector3(35f, 0, 0), 10f, ref now));
            b.SetLimits(MovementLimits.OnFoot);
            Assert.AreEqual(MovementViolationKind.Speed, Simulate(b, new Vector3(35f, 0, 0), 3f, ref now));
        }

        [TestCase("idle", BotMode.Idle)]
        [TestCase("WALK", BotMode.Walk)]
        [TestCase("wander", BotMode.Wander)]
        public void BotMode_Parse(string text, BotMode expected)
        {
            Assert.IsTrue(BotInput.TryParseMode(text, out var mode));
            Assert.AreEqual(expected, mode);
        }

        [Test]
        public void BotMode_ParseUnknown() => Assert.IsFalse(BotInput.TryParseMode("vola", out _));
    }
}
