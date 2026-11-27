using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Underworld.Core;
using Underworld.World;
using UnityEngine;

namespace Underworld.Tests
{
    /// <summary>
    /// Test del mondo costruito a runtime (traccia WORLD): stesso seme = stesso mondo, punti speciali
    /// unici e tutti presenti, punti speciali su terreno calpestabile e non dentro i collider.
    /// </summary>
    public class WorldTests
    {
        /// <summary>Punti speciali che Downtown deve avere (contratto con le altre tracce).</summary>
        static readonly string[] RequiredDowntownAnchors = BuildRequiredList();

        static string[] BuildRequiredList()
        {
            var ids = new List<string>
            {
                "downtown.arrival.from_industrial", "downtown.arrival.from_suburbs", "downtown.arrival.from_outskirts",
                "downtown.exit.industrial", "downtown.exit.suburbs", "downtown.exit.outskirts",
                "downtown.police.station", "downtown.police.release",
                "downtown.bank.lobby", "downtown.bank.vault",
                "downtown.dealership.main", "downtown.dealership.exit",
                "downtown.shop.general", "downtown.robbery.store0",
                "downtown.clothing.main",
                "downtown.garage.public",
                "downtown.mission.board", "downtown.hideout.drop",
            };
            for (int i = 0; i < 8; i++) ids.Add("downtown.spawn." + i);
            for (int i = 0; i < 4; i++)
            {
                ids.Add("downtown.police.spawn." + i);
                ids.Add("downtown.dealership.display." + i);
                ids.Add("downtown.garage.spawn." + i);
                ids.Add("downtown.property.apt" + i);
            }
            return ids.ToArray();
        }

        GameObject _root;
        BuildResult _headless;

        [OneTimeSetUp]
        public void BuildHeadless()
        {
            _headless = BuildArea(AreaId.Downtown, new WorldBuildOptions { Graphics = false }, out _root);
            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void Cleanup() => Destroy(_headless, _root);

        static BuildResult BuildArea(AreaId area, WorldBuildOptions options, out GameObject root)
        {
            root = new GameObject("WorldTestRoot");
            return WorldBuilder.Build(area, root.transform, options);
        }

        static void Destroy(BuildResult result, GameObject root)
        {
            WorldBuilder.Release(result);
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void Downtown_SameSeed_GivesSameWorld()
        {
            var a = BuildArea(AreaId.Downtown, new WorldBuildOptions { Graphics = true, ComputeGeometryHash = true }, out var ra);
            var b = BuildArea(AreaId.Downtown, new WorldBuildOptions { Graphics = true, ComputeGeometryHash = true }, out var rb);
            try
            {
                Assert.AreEqual(a.Hash, b.Hash, "Hash delle operazioni diverso con lo stesso seme");
                Assert.AreEqual(a.GeometryHash, b.GeometryHash, "Geometria diversa con lo stesso seme");
                Assert.AreEqual(a.Colliders, b.Colliders);
                Assert.AreEqual(a.Anchors, b.Anchors);
                Assert.AreEqual(a.Renderers, b.Renderers);
                // Server headless e client devono vedere lo stesso mondo (collider e punti speciali).
                Assert.AreEqual(_headless.Hash, a.Hash, "Hash diverso tra server headless e client");
                Assert.AreEqual(_headless.Colliders, a.Colliders, "Collider diversi tra server headless e client");
                Assert.Greater(a.Renderers, 0, "Il client non ha creato renderer");
            }
            finally
            {
                Destroy(a, ra);
                Destroy(b, rb);
            }
        }

        [Test]
        public void Downtown_Headless_HasNoRenderers()
        {
            Assert.AreEqual(0, _headless.Renderers);
            Assert.AreEqual(0, _root.GetComponentsInChildren<Renderer>(true).Length, "Il server headless ha dei renderer");
            Assert.Greater(_headless.Colliders, 100);
        }

        [Test]
        public void Downtown_AnchorIds_AreUniqueAndWellFormed()
        {
            var seen = new HashSet<string>();
            foreach (var a in _headless.AnchorList)
            {
                Assert.IsTrue(seen.Add(a.Id), "Id duplicato: " + a.Id);
                Assert.IsTrue(a.Id.StartsWith("downtown."), "Id senza area: " + a.Id);
                Assert.GreaterOrEqual(a.Id.Split('.').Length, 3, "Id non nel formato area.tipo.nome: " + a.Id);
            }
        }

        [Test]
        public void Downtown_HasRequiredAnchors()
        {
            var ids = new HashSet<string>();
            foreach (var a in _headless.AnchorList) ids.Add(a.Id);
            var missing = new StringBuilder();
            foreach (var id in RequiredDowntownAnchors)
                if (!ids.Contains(id)) missing.Append(id).Append(' ');
            Assert.IsTrue(missing.Length == 0, "Punti speciali mancanti: " + missing);
        }

        [Test]
        public void Downtown_Exits_PointToTargetAreas()
        {
            Assert.AreEqual(nameof(AreaId.Industrial), FindAnchor("downtown.exit.industrial").Data);
            Assert.AreEqual(nameof(AreaId.Suburbs), FindAnchor("downtown.exit.suburbs").Data);
            Assert.AreEqual(nameof(AreaId.Outskirts), FindAnchor("downtown.exit.outskirts").Data);
            // Uscite sui lati giusti: industriale a est, periferia a nord, campagna a sud.
            Assert.Greater(FindAnchor("downtown.exit.industrial").transform.position.x, 100f);
            Assert.Greater(FindAnchor("downtown.exit.suburbs").transform.position.z, 100f);
            Assert.Less(FindAnchor("downtown.exit.outskirts").transform.position.z, -100f);
        }

        [Test]
        public void Downtown_Anchors_StandOnWalkableGround()
        {
            var bad = new StringBuilder();
            foreach (var a in _headless.AnchorList)
            {
                Vector3 p = a.transform.position;
                // Raggio verso il basso da poco sopra il punto: deve trovare un pavimento quasi piano vicino ai piedi.
                if (!Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out var hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                    bad.Append($"{a.Id} (nessun pavimento) ");
                else if (hit.normal.y < 0.8f || Mathf.Abs(hit.point.y - p.y) > 0.3f)
                    bad.Append($"{a.Id} (pavimento a {hit.point.y - p.y:0.00} m, normale {hit.normal.y:0.00}) ");
            }
            Assert.IsTrue(bad.Length == 0, "Punti speciali non su terreno calpestabile: " + bad);
        }

        [Test]
        public void Downtown_Anchors_AreNotInsideColliders()
        {
            var bad = new StringBuilder();
            foreach (var a in _headless.AnchorList)
            {
                Vector3 p = a.transform.position;
                // Capsula di una persona (raggio 0.3 m, alta 1.8 m) staccata dal pavimento di 0.35 m.
                if (Physics.CheckCapsule(p + Vector3.up * 0.65f, p + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    bad.Append(a.Id).Append(' ');
            }
            Assert.IsTrue(bad.Length == 0, "Punti speciali dentro un collider: " + bad);
        }

        [Test]
        public void Registry_HasDowntownBuilder()
        {
            Assert.IsTrue(AreaBuilderRegistry.TryCreate(AreaId.Downtown, out var builder, out int seed));
            Assert.IsNotNull(builder);
            Assert.AreNotEqual(0, seed);
        }

        AreaAnchor FindAnchor(string id)
        {
            foreach (var a in _headless.AnchorList)
                if (a.Id == id) return a;
            Assert.Fail("Punto speciale mancante: " + id);
            return null;
        }
    }
}
