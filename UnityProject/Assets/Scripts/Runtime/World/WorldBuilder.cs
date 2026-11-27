using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.AI.Navigation;
using Underworld.Core;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Underworld.World
{
    /// <summary>Come costruire il mondo (grafica, NavMesh, seme).</summary>
    public sealed class WorldBuildOptions
    {
        /// <summary>Falso = server headless: solo collider e punti speciali, niente renderer.</summary>
        public bool Graphics = true;
        /// <summary>Costruisce la NavMesh dopo il mondo (solo sul server).</summary>
        public bool BuildNavMesh;
        /// <summary>0 = seme standard dell'area.</summary>
        public int Seed;
        /// <summary>Lato dei pezzi di mondo per l'unione delle mesh (metri).</summary>
        public float ChunkSize = MeshCombiner.DefaultChunkSize;
        /// <summary>Calcola l'hash della geometria visibile (lento: solo per i test).</summary>
        public bool ComputeGeometryHash;
        /// <summary>Tiene le mesh unite leggibili dalla CPU (solo per i test).</summary>
        public bool KeepMeshesReadable;

        /// <summary>
        /// Opzioni giuste per questo processo: senza grafica se è un server (-server) o se non c'è una scheda video
        /// (-batchmode -nographics); NavMesh solo sul server.
        /// </summary>
        public static WorldBuildOptions ForThisProcess()
        {
            var args = LaunchArgs.Current;
            bool noGpu = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            bool server = args.IsServer || (nm != null && nm.IsServer);
            return new WorldBuildOptions
            {
                Graphics = !noGpu && !args.IsServer,
                BuildNavMesh = server,
            };
        }
    }

    /// <summary>Risultato della costruzione di un'area.</summary>
    public sealed class BuildResult
    {
        public AreaId Area;
        public int Seed;
        public bool Graphics;
        public Transform Root;
        public double BuildMs;
        /// <summary>Tempo della NavMesh in ms, -1 se non costruita.</summary>
        public double NavMeshMs = -1;
        public int Renderers, Colliders, Anchors, Shapes, Models, Lights, Vertices, Triangles;
        /// <summary>Hash delle operazioni di costruzione (forme, modelli, collider, punti speciali): uguale su server e client.</summary>
        public ulong Hash;
        /// <summary>Hash della geometria visibile (solo con ComputeGeometryHash).</summary>
        public ulong GeometryHash;
        public readonly List<AreaAnchor> AnchorList = new List<AreaAnchor>();
        public readonly List<PreviewShot> Shots = new List<PreviewShot>();
        public NavMeshSurface NavSurface;
        internal readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();

        public override string ToString() =>
            $"renderers={Renderers}, colliders={Colliders}, anchors={Anchors}, shapes={Shapes}, models={Models}, " +
            $"verts={Vertices}, tris={Triangles}, lights={Lights}, mode={(Graphics ? "client" : "headless")}, seed={Seed}, hash={Hash:X16}";
    }

    /// <summary>
    /// Costruisce un'area sotto un oggetto radice con il suo IAreaBuilder.
    /// Lo usano AreaBuilder (in scena) e i test EditMode.
    /// </summary>
    public static class WorldBuilder
    {
        public static BuildResult Build(AreaId area, Transform root, WorldBuildOptions options)
        {
            options ??= new WorldBuildOptions();
            var sw = Stopwatch.StartNew();
            if (!AreaBuilderRegistry.TryCreate(area, out var builder, out int defaultSeed))
                throw new InvalidOperationException($"[World] Nessun costruttore per l'area {area} (manca [AreaBuilderFor({area})]).");
            var art = ArtLibrary.Load();
            if (art == null)
                throw new InvalidOperationException("[World] ArtLibrary mancante in Resources/World. Esegui il setup (Tools/unity.ps1 -Action setup).");

            int seed = options.Seed != 0 ? options.Seed : defaultSeed;
            var ctx = new BuildContext(area, seed, root, art, options.Graphics, options.ChunkSize);
            try
            {
                builder.Build(ctx);
            }
            catch
            {
                // Costruzione fallita a metà: libera mesh, materiali e texture già creati e svuota la radice.
                ReleaseObjects(ctx.Owned);
                ClearChildren(root);
                throw;
            }

            var result = new BuildResult
            {
                Area = area,
                Seed = seed,
                Graphics = options.Graphics,
                Root = root,
                Colliders = ctx.ColliderCount,
                Anchors = ctx.Anchors.Count,
                Shapes = ctx.ShapeCount,
                Models = ctx.ModelCount,
                Lights = ctx.LightCount,
                Hash = ctx.OperationsHash,
            };
            result.AnchorList.AddRange(ctx.Anchors);
            result.Shots.AddRange(ctx.Shots);
            result.Owned.AddRange(ctx.Owned);

            if (options.Graphics)
            {
                ctx.Combiner.Count(out result.Vertices, out result.Triangles);
                if (options.ComputeGeometryHash)
                {
                    var gh = new WorldHash();
                    ctx.Combiner.HashInto(ref gh);
                    result.GeometryHash = gh.Value;
                }
                var renderRoot = new GameObject("Render").transform;
                renderRoot.SetParent(root, false);
                var created = new List<UnityEngine.Object>();
                result.Renderers = ctx.Combiner.Build(renderRoot, created, options.KeepMeshesReadable).Count;
                result.Owned.AddRange(created);
            }

            sw.Stop();
            result.BuildMs = sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[World] Built {area} in {result.BuildMs:0} ms: {result}");

            if (options.BuildNavMesh) BuildNavMesh(result);
            return result;
        }

        /// <summary>
        /// NavMesh del server (per la polizia): superficie sui collider statici del mondo, agente standard (id 0).
        /// I client non la costruiscono.
        /// </summary>
        public static void BuildNavMesh(BuildResult result)
        {
            if (result == null || result.Root == null) return;
            var sw = Stopwatch.StartNew();
            var surface = result.NavSurface != null ? result.NavSurface : result.Root.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.agentTypeID = 0;
            surface.BuildNavMesh();
            sw.Stop();
            result.NavSurface = surface;
            result.NavMeshMs = sw.Elapsed.TotalMilliseconds;
            if (surface.navMeshData != null) result.Owned.Add(surface.navMeshData);
            var tri = NavMesh.CalculateTriangulation();
            Debug.Log($"[World] NavMesh {result.Area} in {result.NavMeshMs:0} ms: triangles={tri.indices.Length / 3}");
        }

        /// <summary>Distrugge mesh, materiali e texture creati dalla costruzione (quando l'area si chiude).</summary>
        public static void Release(BuildResult result)
        {
            if (result == null) return;
            if (result.NavSurface != null) result.NavSurface.RemoveData();
            ReleaseObjects(result.Owned);
        }

        static void ReleaseObjects(List<UnityEngine.Object> owned)
        {
            foreach (var o in owned)
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
            owned.Clear();
        }

        static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }
    }
}
