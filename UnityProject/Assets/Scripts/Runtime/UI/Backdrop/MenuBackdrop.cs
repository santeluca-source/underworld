using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Underworld.UI
{
    /// <summary>
    /// Sfondo 3D del menu: città notturna low poly costruita a codice con un seme fisso
    /// (palazzi con finestre accese, lampioni, luci rosse sulle torri, auto che passano), camera che si muove piano.
    /// Niente asset esterni: le finestre e le strade sono disegnate negli shader in Assets/UI/Backdrop/Shaders.
    /// Il preset "StreetDay" serve all'anteprima dell'HUD (vista da strada, di giorno, come in gioco).
    /// </summary>
    public sealed class MenuBackdrop : MonoBehaviour
    {
        public enum Preset { MenuNight, StreetDay, StreetNight }

        [Header("Materiali (creati dal passo di setup)")]
        public Material buildingMaterial;
        public Material glowMaterial;
        public Material groundMaterial;
        public Material skyMaterial;

        [Header("Scena")]
        public Camera targetCamera;
        public int seed = 1931;
        public Preset preset = Preset.MenuNight;

        public static MenuBackdrop Instance { get; private set; }

        // Griglia della città: isolati di 38 m, strade di 14 m.
        public const int Blocks = 26;
        public const float Pitch = 52f;
        public const float BlockSize = 38f;
        public static float Half => Blocks * Pitch * 0.5f;

        readonly List<Mesh> _meshes = new List<Mesh>();
        float _startTime;
        float _lastGlowKey = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            Instance = this;
            if (targetCamera == null) targetCamera = GetComponentInChildren<Camera>();
            if (targetCamera == null) targetCamera = Camera.main;
            Build();
            SetPreset(preset);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var m in _meshes) if (m != null) Destroy(m);
            _meshes.Clear();
        }

        // ------------------------------------------------------------------ preset

        public void SetPreset(Preset p)
        {
            preset = p;
            _startTime = Time.unscaledTime;
            bool day = p == Preset.StreetDay;
            Shader.SetGlobalFloat("_UW_DayMix", day ? 1f : 0f);
            if (day)
            {
                Shader.SetGlobalColor("_UW_FogColor", new Color(0.78f, 0.83f, 0.88f));
                Shader.SetGlobalVector("_UW_FogParams", new Vector4(0.0016f, 20f, 0.004f, 0.85f));
                Shader.SetGlobalVector("_UW_LightDir", new Vector4(-0.45f, 0.72f, -0.52f, 0f));
            }
            else
            {
                Shader.SetGlobalColor("_UW_FogColor", new Color(0.235f, 0.188f, 0.2f));
                Shader.SetGlobalVector("_UW_FogParams", new Vector4(0.0019f, 10f, 0.010f, 0.92f));
                Shader.SetGlobalVector("_UW_LightDir", new Vector4(-0.45f, 0.32f, 0.83f, 0f));
            }
            if (targetCamera != null)
            {
                targetCamera.backgroundColor = day ? new Color(0.7f, 0.78f, 0.86f) : new Color(0.03f, 0.035f, 0.06f);
                targetCamera.fieldOfView = p == Preset.MenuNight ? 42f : 58f;
            }
            if (glowMaterial != null) glowMaterial.SetFloat("_Intensity", day ? 0f : 1f);
            UpdateCamera();
        }

        // ------------------------------------------------------------------ camera

        void LateUpdate() => UpdateCamera();

        void UpdateCamera()
        {
            if (targetCamera == null) return;
            float t = Time.unscaledTime - _startTime;
            var cam = targetCamera.transform;
            if (preset == Preset.MenuNight)
            {
                // Giro lentissimo intorno al centro (circa 0,7° al secondo) con un respiro in altezza.
                float angle = (212f + t * 0.7f) * Mathf.Deg2Rad;
                float radius = 600f;
                Vector3 pos = new Vector3(Mathf.Sin(angle) * radius, 118f + Mathf.Sin(t * 0.07f) * 6f, Mathf.Cos(angle) * radius);
                Vector3 look = new Vector3(0f, 82f, 0f);
                Quaternion rot = Quaternion.LookRotation(look - pos, Vector3.up);
                // Il centro della città va a destra: a sinistra c'è il pannello del menu.
                rot = Quaternion.AngleAxis(-15f, Vector3.up) * rot;
                cam.SetPositionAndRotation(pos, rot);
            }
            else
            {
                // Vista da strada, come la camera in terza persona del gioco: avanza piano lungo un viale.
                float x = -Half + Pitch * 13f - (Pitch - BlockSize) * 0.5f + 1.5f;
                Vector3 pos = new Vector3(x, 3.2f, -260f + t * 1.4f);
                cam.SetPositionAndRotation(pos, Quaternion.Euler(-4f, 4f, 0f));
            }

            // Dimensione minima delle luci: circa 2,5 pixel di raggio, qualunque sia la risoluzione.
            float key = targetCamera.fieldOfView * 10000f + targetCamera.pixelHeight;
            if (!Mathf.Approximately(key, _lastGlowKey))
            {
                _lastGlowKey = key;
                float perPixel = 2f * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, targetCamera.pixelHeight);
                Shader.SetGlobalFloat("_UW_MinGlowAngle", perPixel * 2.4f);
            }
        }

        // ------------------------------------------------------------------ costruzione

        void Build()
        {
            var rng = new System.Random(seed);
            var city = new MeshData();
            var glow = new GlowData();
            BuildCity(rng, city, glow);
            BuildStreetLights(rng, glow);
            BuildTraffic(rng, glow);

            AddRenderer("Buildings", city.ToMesh("UW_Buildings"), buildingMaterial, ShadowCastingMode.Off);
            var glowMesh = glow.ToMesh("UW_Glow");
            // Le luci si spostano nel vertex shader: limiti larghi per non farle sparire.
            glowMesh.bounds = new Bounds(new Vector3(0f, 100f, 0f), new Vector3(Half * 2f + 600f, 600f, Half * 2f + 600f));
            AddRenderer("Lights", glowMesh, glowMaterial, ShadowCastingMode.Off);
            AddRenderer("Ground", BuildGround(), groundMaterial, ShadowCastingMode.Off);
            var sky = BuildSkySphere();
            sky.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            AddRenderer("Sky", sky, skyMaterial, ShadowCastingMode.Off);

            if (groundMaterial != null)
            {
                groundMaterial.SetFloat("_Pitch", Pitch);
                groundMaterial.SetFloat("_Block", BlockSize);
                groundMaterial.SetVector("_Origin", new Vector4(-Half, -Half, 0f, 0f));
            }
        }

        void AddRenderer(string name, Mesh mesh, Material mat, ShadowCastingMode shadows)
        {
            _meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static float R(System.Random rng) => (float)rng.NextDouble();
        static float R(System.Random rng, float a, float b) => a + (b - a) * (float)rng.NextDouble();

        void BuildCity(System.Random rng, MeshData city, GlowData glow)
        {
            float half = Half;
            for (int bx = 0; bx < Blocks; bx++)
            for (int bz = 0; bz < Blocks; bz++)
            {
                float x0 = -half + bx * Pitch;
                float z0 = -half + bz * Pitch;
                Vector2 c = new Vector2(x0 + BlockSize * 0.5f, z0 + BlockSize * 0.5f);
                float d = c.magnitude / half;
                if (d > 1.02f) continue;              // città rotonda: i bordi si perdono nella foschia
                if (d > 0.18f && R(rng) < 0.05f) continue; // qualche piazza o parco

                float core = Mathf.Clamp01(1f - d / 0.42f);
                float mid = Mathf.Clamp01(1f - d / 0.85f);
                int nx = core > 0.3f ? rng.Next(1, 3) : rng.Next(2, 4);
                int nz = core > 0.3f ? rng.Next(1, 3) : rng.Next(2, 4);
                float lotW = BlockSize / nx;
                float lotD = BlockSize / nz;
                for (int lx = 0; lx < nx; lx++)
                for (int lz = 0; lz < nz; lz++)
                {
                    if (R(rng) < 0.06f) continue;
                    float r = R(rng);
                    float h = R(rng, 8f, 24f) + mid * Mathf.Lerp(8f, 55f, r * r) + core * Mathf.Lerp(18f, 200f, Mathf.Pow(r, 2.1f));
                    if (R(rng) < 0.015f + core * 0.05f) h *= 1.55f; // poche torri simbolo
                    float inset = core > 0.3f ? R(rng, 2f, 5f) : R(rng, 1.2f, 3f);
                    float bx0 = x0 + lx * lotW + inset;
                    float bx1 = x0 + (lx + 1) * lotW - inset;
                    float bz0 = z0 + lz * lotD + inset;
                    float bz1 = z0 + (lz + 1) * lotD - inset;
                    if (bx1 - bx0 < 5f || bz1 - bz0 < 5f) continue;
                    AddBuilding(rng, city, glow, bx0, bz0, bx1, bz1, h, core);
                }
            }
        }

        void AddBuilding(System.Random rng, MeshData city, GlowData glow, float x0, float z0, float x1, float z1, float h, float core)
        {
            float floorH = core > 0.3f ? R(rng, 3.4f, 4.0f) : R(rng, 2.9f, 3.4f);
            h = Mathf.Max(floorH * 2f, Mathf.Round(h / floorH) * floorH) + 0.6f; // parapetto sopra l'ultimo piano
            float seedValue = R(rng, 0f, 100f);
            bool office = core > 0.25f && R(rng) < 0.7f;
            float lit = office ? R(rng, 0.22f, 0.48f) : R(rng, 0.14f, 0.4f);
            float warmth = office ? R(rng, 0.2f, 0.55f) : R(rng, 0.7f, 0.95f);
            var data = new Vector4(seedValue, lit, warmth, 0f);
            float bay = office ? R(rng, 2.6f, 3.2f) : R(rng, 3.0f, 3.8f);

            float top = h;
            if (h > 60f && R(rng) < 0.55f)
            {
                // Rientro: base larga, torre più stretta sopra.
                float baseH = Mathf.Round(h * R(rng, 0.35f, 0.6f) / floorH) * floorH + 0.6f;
                city.AddBox(x0, 0f, z0, x1, baseH, z1, floorH, bay, data);
                float shrink = R(rng, 0.12f, 0.24f);
                float sx = (x1 - x0) * shrink, sz = (z1 - z0) * shrink;
                x0 += sx; x1 -= sx; z0 += sz; z1 -= sz;
                city.AddBox(x0, baseH, z0, x1, h, z1, floorH, bay, data);
            }
            else
            {
                city.AddBox(x0, 0f, z0, x1, h, z1, floorH, bay, data);
            }

            if (h > 115f)
            {
                // Corona scura e antenna con le luci rosse per gli aerei.
                float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
                float cw = (x1 - x0) * 0.32f, cd = (z1 - z0) * 0.32f;
                float crownH = R(rng, 4f, 9f);
                city.AddBox(cx - cw, top, cz - cd, cx + cw, top + crownH, cz + cd, floorH, bay, new Vector4(seedValue, 0f, 0f, 0f));
                float antenna = R(rng, 10f, 28f);
                city.AddBox(cx - 0.45f, top + crownH, cz - 0.45f, cx + 0.45f, top + crownH + antenna, cz + 0.45f, floorH, bay, new Vector4(seedValue, 0f, 0f, 1f));
                float phase = R(rng);
                var red = new Color(1f, 0.09f, 0.05f, 3.2f);
                glow.Add(new Vector3(cx, top + crownH + antenna + 0.6f, cz), red, 0.9f, phase, 1f, 0.55f);
                glow.Add(new Vector3(x0, top + 0.8f, z0), red, 0.6f, phase, 1f, 0.55f);
                glow.Add(new Vector3(x1, top + 0.8f, z1), red, 0.6f, phase, 1f, 0.55f);
            }
            else if (R(rng) < 0.35f)
            {
                // Macchinari sul tetto.
                float cx = R(rng, x0 + 2f, x1 - 4f), cz = R(rng, z0 + 2f, z1 - 4f);
                city.AddBox(cx, top, cz, cx + R(rng, 2f, 4f), top + R(rng, 1.2f, 2.6f), cz + R(rng, 2f, 4f), floorH, bay, new Vector4(seedValue, 0f, 0f, 1f));
            }
        }

        void BuildStreetLights(System.Random rng, GlowData glow)
        {
            float half = Half;
            float street = Pitch - BlockSize;
            var sodium = new Color(1f, 0.6f, 0.28f, 2.6f);
            var pool = new Color(1f, 0.5f, 0.2f, 0.11f);
            for (int i = 0; i <= Blocks; i++)
            {
                // Centro della strada i (tra l'isolato i-1 e l'isolato i).
                float line = -half + i * Pitch - street * 0.5f;
                for (float s = -half; s <= half; s += 26f)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float off = side * (street * 0.5f - 1.4f);
                        var a = new Vector3(line + off, 0f, s);
                        var b = new Vector3(s, 0f, line + off);
                        foreach (var p in new[] { a, b })
                        {
                            float d = new Vector2(p.x, p.z).magnitude / half;
                            if (d > 0.98f) continue;
                            glow.Add(p + Vector3.up * 7.2f, sodium, 0.55f, 0f, 0f, 0f);
                            if (d < 0.75f) glow.AddGround(p + Vector3.up * 0.05f, pool, 8f);
                        }
                    }
                }
            }
        }

        void BuildTraffic(System.Random rng, GlowData glow)
        {
            float half = Half;
            float street = Pitch - BlockSize;
            var head = new Color(1f, 0.9f, 0.75f, 2.2f);
            var tail = new Color(1f, 0.08f, 0.04f, 2.0f);
            for (int i = 1; i < Blocks; i += 3)
            {
                float line = -half + i * Pitch - street * 0.5f;
                for (int axis = 0; axis < 2; axis++)
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    int cars = 7;
                    for (int c = 0; c < cars; c++)
                    {
                        float phase = R(rng);
                        float speed = R(rng, 11f, 16f) / (half * 2f);
                        float lane = dir * 2.6f;
                        Vector3 start, motion, across;
                        if (axis == 0)
                        {
                            start = new Vector3(line + lane, 0.7f, dir > 0 ? -half : half);
                            motion = new Vector3(0f, 0f, dir * half * 2f);
                            across = Vector3.right * 0.75f;
                        }
                        else
                        {
                            start = new Vector3(dir > 0 ? -half : half, 0.7f, line + lane);
                            motion = new Vector3(dir * half * 2f, 0f, 0f);
                            across = Vector3.forward * 0.75f;
                        }
                        // Una direzione mostra i fari, l'altra le luci rosse (la camera vede entrambe).
                        var col = dir > 0 ? head : tail;
                        glow.AddMoving(start - across, col, 0.35f, phase, motion, speed);
                        glow.AddMoving(start + across, col, 0.35f, phase, motion, speed);
                    }
                }
            }
        }

        Mesh BuildGround()
        {
            float s = Half * 4f;
            var m = new Mesh { name = "UW_Ground" };
            m.vertices = new[] { new Vector3(-s, 0f, -s), new Vector3(-s, 0f, s), new Vector3(s, 0f, s), new Vector3(s, 0f, -s) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        static Mesh BuildSkySphere()
        {
            const int lon = 32, lat = 16;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float v = Mathf.PI * y / lat;
                for (int x = 0; x <= lon; x++)
                {
                    float u = 2f * Mathf.PI * x / lon;
                    verts.Add(new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u)));
                }
            }
            for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int a = y * (lon + 1) + x, b = a + lon + 1;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
            var m = new Mesh { name = "UW_Sky" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            return m;
        }

        // ------------------------------------------------------------------ dati delle mesh

        /// <summary>Palazzi: scatole con le coordinate della facciata in campate e piani.</summary>
        sealed class MeshData
        {
            readonly List<Vector3> _v = new List<Vector3>(60000);
            readonly List<Vector3> _n = new List<Vector3>(60000);
            readonly List<Vector2> _uv = new List<Vector2>(60000);
            readonly List<Vector4> _data = new List<Vector4>(60000);
            readonly List<int> _t = new List<int>(90000);

            public void AddBox(float x0, float y0, float z0, float x1, float y1, float z1, float floorH, float bayTarget, Vector4 data)
            {
                float v0 = y0 / floorH, v1 = y1 / floorH;
                float bx = Mathf.Max(1f, Mathf.Round((x1 - x0) / bayTarget));
                float bz = Mathf.Max(1f, Mathf.Round((z1 - z0) / bayTarget));
                var facade = new Vector4(data.x, data.y, data.z, data.w);
                // Angoli in ordine: basso-sinistra, alto-sinistra, alto-destra, basso-destra (visti da fuori).
                Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back, bx, v0, v1, facade);
                Quad(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.forward, bx, v0, v1, facade);
                Quad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), Vector3.left, bz, v0, v1, facade);
                Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right, bz, v0, v1, facade);
                // Tetto.
                int i = _v.Count;
                _v.Add(new Vector3(x0, y1, z0)); _v.Add(new Vector3(x0, y1, z1)); _v.Add(new Vector3(x1, y1, z1)); _v.Add(new Vector3(x1, y1, z0));
                for (int k = 0; k < 4; k++) { _n.Add(Vector3.up); _data.Add(new Vector4(data.x, 0f, 0f, 1f)); }
                _uv.Add(new Vector2(x0, z0) / 4f); _uv.Add(new Vector2(x0, z1) / 4f); _uv.Add(new Vector2(x1, z1) / 4f); _uv.Add(new Vector2(x1, z0) / 4f);
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            void Quad(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Vector3 n, float bays, float v0, float v1, Vector4 data)
            {
                int i = _v.Count;
                _v.Add(bl); _v.Add(tl); _v.Add(tr); _v.Add(br);
                for (int k = 0; k < 4; k++) { _n.Add(n); _data.Add(data); }
                _uv.Add(new Vector2(0f, v0)); _uv.Add(new Vector2(0f, v1)); _uv.Add(new Vector2(bays, v1)); _uv.Add(new Vector2(bays, v0));
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.SetUVs(0, _uv);
                m.SetUVs(1, _data);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>Luci: quadrati con il centro uguale nei 4 vertici (il vertex shader li apre).</summary>
        sealed class GlowData
        {
            readonly List<Vector3> _v = new List<Vector3>(40000);
            readonly List<Color> _c = new List<Color>(40000);
            readonly List<Vector2> _corner = new List<Vector2>(40000);
            readonly List<Vector4> _data = new List<Vector4>(40000);
            readonly List<Vector4> _motion = new List<Vector4>(40000);
            readonly List<int> _t = new List<int>(60000);

            static readonly Vector2[] Corners = { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) };

            void AddQuad(Vector3 center, Color color, Vector4 data, Vector4 motion)
            {
                int i = _v.Count;
                for (int k = 0; k < 4; k++)
                {
                    _v.Add(center);
                    _c.Add(color);
                    _corner.Add(Corners[k]);
                    _data.Add(data);
                    _motion.Add(motion);
                }
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            /// <summary>Luce rivolta alla camera. kind 0 = fissa, 1 = lampeggia (blinkSpeed in Hz).</summary>
            public void Add(Vector3 center, Color color, float size, float phase, float kind, float blinkSpeed)
                => AddQuad(center, color, new Vector4(size, phase, kind, blinkSpeed), Vector4.zero);

            /// <summary>Luce che corre lungo <paramref name="motion"/> (giri al secondo = speed).</summary>
            public void AddMoving(Vector3 start, Color color, float size, float phase, Vector3 motion, float speed)
                => AddQuad(start, color, new Vector4(size, phase, 2f, 0f), new Vector4(motion.x, motion.y, motion.z, speed));

            /// <summary>Pozza di luce stesa a terra.</summary>
            public void AddGround(Vector3 center, Color color, float size)
                => AddQuad(center, color, new Vector4(size, 0f, 3f, 0f), Vector4.zero);

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(_v);
                m.SetColors(_c);
                m.SetUVs(0, _corner);
                m.SetUVs(1, _data);
                m.SetUVs(2, _motion);
                m.SetTriangles(_t, 0);
                return m;
            }
        }
    }
}
