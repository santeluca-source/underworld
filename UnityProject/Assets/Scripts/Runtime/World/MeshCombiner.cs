using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Underworld.World
{
    /// <summary>Facce di una scatola da disegnare (le facce nascoste si possono saltare).</summary>
    [Flags]
    public enum BoxFaces : byte
    {
        None = 0,
        PosX = 1, NegX = 2, PosY = 4, NegY = 8, PosZ = 16, NegZ = 32,
        All = PosX | NegX | PosY | NegY | PosZ | NegZ,
        /// <summary>Tutto tranne il fondo (per le cose appoggiate a terra).</summary>
        NoBottom = All & ~NegY,
        /// <summary>Solo i lati (né sopra né sotto).</summary>
        Sides = PosX | NegX | PosZ | NegZ,
    }

    /// <summary>Dati di una mesh sorgente letti una volta (vertici e indici per ogni sotto-mesh).</summary>
    public sealed class MeshSource
    {
        public readonly Vector3[] Vertices;
        public readonly Vector3[] Normals;
        public readonly Vector2[] Uvs;
        /// <summary>Per ogni sotto-mesh: i vertici usati (indici nella mesh).</summary>
        public readonly int[][] SubVertices;
        /// <summary>Per ogni sotto-mesh: i triangoli con indici locali (0..SubVertices.Length-1).</summary>
        public readonly int[][] SubIndices;

        public MeshSource(Mesh mesh)
        {
            Vertices = mesh.vertices;
            Normals = mesh.normals;
            Uvs = mesh.uv;
            if (Normals == null || Normals.Length != Vertices.Length) Normals = new Vector3[Vertices.Length];
            if (Uvs == null || Uvs.Length != Vertices.Length) Uvs = new Vector2[Vertices.Length];

            int subCount = mesh.subMeshCount;
            SubVertices = new int[subCount][];
            SubIndices = new int[subCount][];
            var remap = new int[Vertices.Length];
            for (int s = 0; s < subCount; s++)
            {
                for (int i = 0; i < remap.Length; i++) remap[i] = -1;
                var tris = mesh.GetTriangles(s);
                var used = new List<int>(tris.Length);
                var local = new int[tris.Length];
                for (int t = 0; t < tris.Length; t++)
                {
                    int v = tris[t];
                    if (remap[v] < 0) { remap[v] = used.Count; used.Add(v); }
                    local[t] = remap[v];
                }
                SubVertices[s] = used.ToArray();
                SubIndices[s] = local;
            }
        }
    }

    /// <summary>
    /// Un secchio di geometria: un materiale in un pezzo di mondo (chunk). Diventa una sola mesh.
    /// </summary>
    public sealed class MeshBucket
    {
        public readonly Material Material;
        public readonly int ChunkX, ChunkZ;
        public readonly bool Global;

        internal readonly List<Vector3> V = new List<Vector3>(512);
        internal readonly List<Vector3> N = new List<Vector3>(512);
        internal readonly List<Vector2> UV = new List<Vector2>(512);
        internal readonly List<int> I = new List<int>(768);

        internal MeshBucket(Material material, int cx, int cz, bool global)
        {
            Material = material;
            ChunkX = cx;
            ChunkZ = cz;
            Global = global;
        }

        public int VertexCount => V.Count;
        public int TriangleCount => I.Count / 3;

        /// <summary>
        /// Quadrilatero piatto. I quattro punti vanno in giro (orario o antiorario):
        /// l'ordine dei triangoli si sceglie dalla normale, così la faccia guarda sempre fuori.
        /// </summary>
        public void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Vector2 uv)
        {
            int i = V.Count;
            V.Add(p0); V.Add(p1); V.Add(p2); V.Add(p3);
            N.Add(normal); N.Add(normal); N.Add(normal); N.Add(normal);
            UV.Add(uv); UV.Add(uv); UV.Add(uv); UV.Add(uv);
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) >= 0f)
            {
                I.Add(i); I.Add(i + 1); I.Add(i + 2);
                I.Add(i); I.Add(i + 2); I.Add(i + 3);
            }
            else
            {
                I.Add(i); I.Add(i + 2); I.Add(i + 1);
                I.Add(i); I.Add(i + 3); I.Add(i + 2);
            }
        }

        /// <summary>Triangolo piatto con la faccia verso la normale data.</summary>
        public void AddTri(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 normal, Vector2 uv)
        {
            int i = V.Count;
            V.Add(p0); V.Add(p1); V.Add(p2);
            N.Add(normal); N.Add(normal); N.Add(normal);
            UV.Add(uv); UV.Add(uv); UV.Add(uv);
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) >= 0f) { I.Add(i); I.Add(i + 1); I.Add(i + 2); }
            else { I.Add(i); I.Add(i + 2); I.Add(i + 1); }
        }

        /// <summary>Scatola orientata con un colore solo (uv fisso).</summary>
        public void AddBox(Vector3 center, Vector3 size, Quaternion rot, Vector2 uv, BoxFaces faces)
        {
            Vector3 ex = rot * new Vector3(size.x * 0.5f, 0, 0);
            Vector3 ey = rot * new Vector3(0, size.y * 0.5f, 0);
            Vector3 ez = rot * new Vector3(0, 0, size.z * 0.5f);
            Vector3 nx = rot * Vector3.right, ny = rot * Vector3.up, nz = rot * Vector3.forward;

            if ((faces & BoxFaces.PosX) != 0) Face(center + ex, ez, ey, nx, uv);
            if ((faces & BoxFaces.NegX) != 0) Face(center - ex, ez, ey, -nx, uv);
            if ((faces & BoxFaces.PosY) != 0) Face(center + ey, ex, ez, ny, uv);
            if ((faces & BoxFaces.NegY) != 0) Face(center - ey, ex, ez, -ny, uv);
            if ((faces & BoxFaces.PosZ) != 0) Face(center + ez, ex, ey, nz, uv);
            if ((faces & BoxFaces.NegZ) != 0) Face(center - ez, ex, ey, -nz, uv);
        }

        void Face(Vector3 c, Vector3 a, Vector3 b, Vector3 n, Vector2 uv)
            => AddQuad(c - a - b, c - a + b, c + a + b, c + a - b, n, uv);

        /// <summary>
        /// Prisma verticale a N lati (cilindro low poly). <paramref name="baseCenter"/> = centro del fondo.
        /// </summary>
        public void AddPrism(Vector3 baseCenter, float radius, float height, int sides, float startDeg, Vector2 uv, bool top, bool bottom)
        {
            sides = Mathf.Max(3, sides);
            Vector3 up = Vector3.up * height;
            float step = Mathf.PI * 2f / sides;
            float a0 = startDeg * Mathf.Deg2Rad;
            for (int i = 0; i < sides; i++)
            {
                float a = a0 + i * step, b = a0 + (i + 1) * step, mid = (a + b) * 0.5f;
                Vector3 pa = baseCenter + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                Vector3 pb = baseCenter + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius;
                Vector3 n = new Vector3(Mathf.Cos(mid), 0, Mathf.Sin(mid));
                AddQuad(pa, pa + up, pb + up, pb, n, uv);
                if (top) AddTri(baseCenter + up, pa + up, pb + up, Vector3.up, uv);
                if (bottom) AddTri(baseCenter, pa, pb, Vector3.down, uv);
            }
        }

        /// <summary>
        /// Copia una sotto-mesh trasformata. <paramref name="constantUv"/>: se non null tutti i vertici usano quel colore
        /// della tavolozza. <paramref name="shiftRegion"/>/<paramref name="shift"/>: sposta le uv dentro la regione (cambio colore).
        /// </summary>
        public void AddMesh(MeshSource src, int submesh, Matrix4x4 m, Vector2? constantUv, Rect shiftRegion, Vector2 shift)
        {
            var verts = src.SubVertices[submesh];
            var idx = src.SubIndices[submesh];
            int baseIndex = V.Count;
            bool doShift = shift != Vector2.zero;
            bool flip = m.determinant < 0f;
            for (int k = 0; k < verts.Length; k++)
            {
                int v = verts[k];
                V.Add(m.MultiplyPoint3x4(src.Vertices[v]));
                N.Add(m.MultiplyVector(src.Normals[v]).normalized);
                if (constantUv.HasValue) UV.Add(constantUv.Value);
                else
                {
                    var uv = src.Uvs[v];
                    if (doShift && shiftRegion.Contains(uv)) uv += shift;
                    UV.Add(uv);
                }
            }
            for (int t = 0; t < idx.Length; t += 3)
            {
                I.Add(baseIndex + idx[t]);
                if (flip) { I.Add(baseIndex + idx[t + 2]); I.Add(baseIndex + idx[t + 1]); }
                else { I.Add(baseIndex + idx[t + 1]); I.Add(baseIndex + idx[t + 2]); }
            }
        }
    }

    /// <summary>
    /// Unisce la geometria statica per materiale in pezzi di mondo di circa 64 m (indici a 32 bit).
    /// Così una vista di strada costa poche draw call anche con migliaia di forme.
    /// Le forme fatte a codice e i modelli della libreria finiscono qui; con <see cref="AbsorbRenderers"/>
    /// si possono unire anche renderer statici creati a mano.
    /// </summary>
    public sealed class MeshCombiner
    {
        public const float DefaultChunkSize = 64f;

        readonly struct Key : IEquatable<Key>
        {
            readonly int _cx, _cz, _mat;
            public Key(int cx, int cz, Material m) { _cx = cx; _cz = cz; _mat = m != null ? m.GetInstanceID() : 0; }
            public bool Equals(Key o) => _cx == o._cx && _cz == o._cz && _mat == o._mat;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => (_cx * 73856093) ^ (_cz * 19349663) ^ (_mat * 83492791);
        }

        const int GlobalChunk = int.MinValue;

        public readonly float ChunkSize;
        readonly Dictionary<Key, MeshBucket> _buckets = new Dictionary<Key, MeshBucket>();
        readonly List<MeshBucket> _order = new List<MeshBucket>();
        readonly Dictionary<Mesh, MeshSource> _sources = new Dictionary<Mesh, MeshSource>();

        public MeshCombiner(float chunkSize = DefaultChunkSize) => ChunkSize = Mathf.Max(8f, chunkSize);

        public IReadOnlyList<MeshBucket> Buckets => _order;

        /// <summary>
        /// Secchio per un materiale nel chunk che contiene <paramref name="position"/>.
        /// Le cose più grandi di un chunk (es. il terreno) vanno nel secchio "globale" del materiale.
        /// </summary>
        public MeshBucket GetBucket(Material material, Vector3 position, float extent)
        {
            bool global = extent > ChunkSize;
            int cx = global ? GlobalChunk : Mathf.FloorToInt(position.x / ChunkSize);
            int cz = global ? GlobalChunk : Mathf.FloorToInt(position.z / ChunkSize);
            var key = new Key(cx, cz, material);
            if (!_buckets.TryGetValue(key, out var b))
            {
                b = new MeshBucket(material, cx, cz, global);
                _buckets.Add(key, b);
                _order.Add(b);
            }
            return b;
        }

        /// <summary>Dati della mesh sorgente (letti una volta per mesh). La mesh deve essere leggibile.</summary>
        public MeshSource Source(Mesh mesh)
        {
            if (!_sources.TryGetValue(mesh, out var s))
            {
                s = new MeshSource(mesh);
                _sources.Add(mesh, s);
            }
            return s;
        }

        /// <summary>
        /// Unisce i MeshRenderer statici sotto <paramref name="root"/> nei secchi (per materiale e chunk)
        /// e poi li distrugge. Utile per arredi creati a mano con renderer propri.
        /// </summary>
        public int AbsorbRenderers(Transform root)
        {
            int count = 0;
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var src = Source(mf.sharedMesh);
                var m = r.transform.localToWorldMatrix;
                var mats = r.sharedMaterials;
                var bounds = r.bounds;
                float extent = Mathf.Max(bounds.size.x, bounds.size.z);
                for (int s = 0; s < src.SubIndices.Length; s++)
                {
                    var mat = mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                    if (mat == null) continue;
                    GetBucket(mat, bounds.center, extent).AddMesh(src, s, m, null, default, Vector2.zero);
                }
                DestroyObject(mf);
                DestroyObject(r);
                count++;
            }
            return count;
        }

        /// <summary>Statistiche prima della costruzione.</summary>
        public void Count(out int vertices, out int triangles)
        {
            vertices = 0;
            triangles = 0;
            foreach (var b in _order) { vertices += b.VertexCount; triangles += b.TriangleCount; }
        }

        /// <summary>Hash della geometria (posizioni quantizzate al mm, uv, indici), uguale se il mondo è uguale.</summary>
        public void HashInto(ref WorldHash hash)
        {
            foreach (var b in _order)
            {
                hash.Add(b.ChunkX); hash.Add(b.ChunkZ); hash.Add(b.Material != null ? b.Material.name : "-");
                hash.Add(b.V.Count); hash.Add(b.I.Count);
                for (int i = 0; i < b.V.Count; i++) { hash.Add(b.V[i]); hash.Add(b.UV[i]); }
                for (int i = 0; i < b.I.Count; i++) hash.Add(b.I[i]);
            }
        }

        /// <summary>
        /// Crea un oggetto con MeshFilter + MeshRenderer per ogni secchio non vuoto.
        /// <paramref name="created"/> riceve le mesh create (da distruggere quando l'area si chiude).
        /// </summary>
        public List<MeshRenderer> Build(Transform parent, List<UnityEngine.Object> created, bool keepReadable)
        {
            var result = new List<MeshRenderer>(_order.Count);
            foreach (var b in _order)
            {
                if (b.V.Count == 0) continue;
                string matName = b.Material != null ? b.Material.name : "null";
                string chunkName = b.Global ? "Global" : $"{b.ChunkX}_{b.ChunkZ}";
                var mesh = new Mesh { name = $"World_{chunkName}_{matName}", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(b.V);
                mesh.SetNormals(b.N);
                mesh.SetUVs(0, b.UV);
                mesh.SetTriangles(b.I, 0, false);
                mesh.RecalculateBounds();
                if (!keepReadable) mesh.UploadMeshData(true);
                created?.Add(mesh);

                var go = new GameObject($"Chunk_{chunkName}_{matName}") { isStatic = true };
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = b.Material;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Simple;
                result.Add(mr);
            }
            return result;
        }

        static void DestroyObject(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
