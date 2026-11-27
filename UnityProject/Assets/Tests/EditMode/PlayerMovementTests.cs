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

        // ---- Posizioni impossibili (NaN, infinito, fuori dal mondo) ----

        [Test]
        public void NonFiniteDelta_IsTeleport_AndBudgetStaysFinite()
        {
            var b = new MovementBudget(MovementLimits.OnFoot);
            b.Reset(0);
            b.Advance(0.033);
            Assert.AreEqual(MovementViolationKind.Teleport, b.Consume(new Vector3(float.NaN, float.NaN, float.NaN)));
            Assert.AreEqual(MovementViolationKind.Teleport, b.Consume(new Vector3(float.PositiveInfinity, 0, 0)));
            Assert.AreEqual(MovementViolationKind.Teleport, b.Consume(new Vector3(0, float.NegativeInfinity, 0)));
            Assert.IsTrue(float.IsFinite(b.Horizontal) && float.IsFinite(b.Up) && float.IsFinite(b.Down));
            // Dopo il NaN il controllo funziona ancora.
            b.Advance(0.066);
            Assert.AreEqual(MovementViolationKind.Teleport, b.Consume(new Vector3(50f, 0, 0)));
        }

        [Test]
        public void SanePosition_RejectsNaNInfinityAndFarAway()
        {
            var l = MovementLimits.OnFoot;
            Assert.IsTrue(l.IsSanePosition(new Vector3(120f, 3f, -40f)));
            Assert.IsFalse(l.IsSanePosition(new Vector3(float.NaN, 0, 0)));
            Assert.IsFalse(l.IsSanePosition(new Vector3(0, float.PositiveInfinity, 0)));
            Assert.IsFalse(l.IsSanePosition(new Vector3(0, 0, 1e6f)));
        }

        // ---- Volo e caduta (AirborneTracker) ----

        /// <summary>Simula un movimento verticale y(t) in aria (grounded falso) a 30 campioni al secondo.</summary>
        static MovementViolationKind SimulateAir(AirborneTracker air, System.Func<float, float> y, float seconds, MovementLimits l)
        {
            const float dt = 1f / 30f;
            for (float t = dt; t <= seconds; t += dt)
            {
                var k = air.Step(new Vector3(0, y(t), 0), false, t, l);
                if (k != MovementViolationKind.None) return k;
            }
            return MovementViolationKind.None;
        }

        [Test]
        public void Air_RealJump_IsAllowed()
        {
            var l = MovementLimits.OnFoot;
            var air = new AirborneTracker();
            air.Reset(0f);
            float v0 = Mathf.Sqrt(1.3f * 2f * 22f);
            // Salto vero (1,3 m, gravità 22) fino a tornare a terra.
            Assert.AreEqual(MovementViolationKind.None, SimulateAir(air, t => Mathf.Max(0f, v0 * t - 11f * t * t), 0.69f, l));
            Assert.AreEqual(MovementViolationKind.None, air.Step(Vector3.zero, true, 0.7, l));
            Assert.IsFalse(air.Airborne);
        }

        [Test]
        public void Air_LongFallFromBuilding_IsAllowed()
        {
            var l = MovementLimits.OnFoot;
            var air = new AirborneTracker();
            air.Reset(40f);
            Assert.AreEqual(MovementViolationKind.None, SimulateAir(air, t => 40f - Mathf.Min(11f * t * t, 50f * t), 2.5f, l));
        }

        [Test]
        public void Air_SlowClimbIsDetected()
        {
            var l = MovementLimits.OnFoot;
            var air = new AirborneTracker();
            air.Reset(0f);
            // 5,9 m/s in su: il credito della salita lo permetterebbe, il controllo del volo no.
            Assert.AreEqual(MovementViolationKind.Vertical, SimulateAir(air, t => 5.9f * t, 3f, l));
        }

        [Test]
        public void Air_HoverIsDetected()
        {
            var l = MovementLimits.OnFoot;
            var air = new AirborneTracker();
            air.Reset(0f);
            // Sale di 1 m e poi resta sospeso.
            Assert.AreEqual(MovementViolationKind.Vertical, SimulateAir(air, t => Mathf.Min(1f, 4f * t), 4f, l));
        }

        [Test]
        public void Air_InstantDropUnderTheMap_IsDetected()
        {
            var l = MovementLimits.OnFoot;
            var air = new AirborneTracker();
            air.Reset(0f);
            Assert.AreEqual(MovementViolationKind.Vertical, air.Step(new Vector3(0, -15f, 0), false, 0.033, l));
        }

        [Test]
        public void Air_BelowMinY_IsDetected_FlightAllowanceSkipsChecks()
        {
            var l = MovementLimits.OnFoot;
            var air = new AirborneTracker();
            air.Reset(0f);
            Assert.AreEqual(MovementViolationKind.Vertical, air.Step(new Vector3(0, l.MinY - 1f, 0), true, 0.1, l));

            var ladder = MovementLimits.OnFoot;
            ladder.AllowFlight = true;
            air.Reset(0f);
            Assert.AreEqual(MovementViolationKind.None, SimulateAir(air, t => 3f * t, 5f, ladder));
        }

        [Test]
        public void DownCapacity_IsBelowAnInstantDropUnderTheGround()
        {
            // Prima era 56,5 m in un colpo: un tuffo di 50 m sotto la mappa passava.
            var b = new MovementBudget(MovementLimits.OnFoot);
            b.Reset(0);
            b.Advance(0.033);
            Assert.AreEqual(MovementViolationKind.Teleport, b.Consume(new Vector3(0, -50f, 0)));
        }

        // ---- Finestra delle violazioni ----

        [Test]
        public void ViolationWindow_CountsOnlyRecentViolations()
        {
            var w = new ViolationWindow(30f);
            Assert.AreEqual(1, w.Add(0));
            Assert.AreEqual(2, w.Add(10));
            Assert.AreEqual(3, w.Add(20));
            Assert.AreEqual(3, w.Add(35)); // quella a 0 s è uscita dalla finestra
            Assert.AreEqual(1, w.Count(60));
        }

        // ---- Correzioni: solo il server ----

        [Test]
        public void CorrectPositionRpc_OnlyServerMayInvoke()
        {
            var m = typeof(MovementValidator).GetMethod("CorrectPositionRpc",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(m, "CorrectPositionRpc");
            var attr = (Unity.Netcode.RpcAttribute)System.Attribute.GetCustomAttribute(m, typeof(Unity.Netcode.RpcAttribute));
            Assert.IsNotNull(attr, "[Rpc]");
            Assert.AreEqual(Unity.Netcode.RpcInvokePermission.Server, attr.InvokePermission);
            var ps = m.GetParameters();
            Assert.AreEqual(typeof(Unity.Netcode.RpcParams), ps[ps.Length - 1].ParameterType, "serve RpcParams per leggere il mittente");

            Assert.IsTrue(MovementValidator.IsTrustedCorrectionSender(Unity.Netcode.NetworkManager.ServerClientId));
            Assert.IsFalse(MovementValidator.IsTrustedCorrectionSender(3));
        }
    }
}
