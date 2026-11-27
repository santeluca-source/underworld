using System;
using System.Collections.Generic;
using Underworld.Core;
using UnityEngine;

namespace Underworld.World
{
    /// <summary>Inquadratura per il modo anteprima (-preview): posizione della camera e punto guardato.</summary>
    public readonly struct PreviewShot
    {
        public readonly string Name;
        public readonly Vector3 Position;
        public readonly Vector3 LookAt;

        public PreviewShot(string name, Vector3 position, Vector3 lookAt)
        {
            Name = name;
            Position = position;
            LookAt = lookAt;
        }
    }

    public enum TextAlign { Center, Left, Right }

    /// <summary>
    /// Strumenti per costruire un'area: forme con i colori della tavolozza, modelli della libreria,
    /// collider, punti speciali (AreaAnchor), luci, scritte. Tutto quello che si vede finisce nel MeshCombiner.
    /// Sul server senza grafica (Graphics = false) si creano solo collider e punti speciali:
    /// le chiamate sono le stesse, così il mondo è identico su server e client.
    /// </summary>
    public sealed class BuildContext
    {
        public AreaId Area { get; }
        /// <summary>Prefisso degli id dei punti speciali, es. "downtown".</summary>
        public string AreaKey { get; }
        public int Seed { get; }
        /// <summary>Unica fonte di numeri casuali del mondo. Non dipende dalla grafica.</summary>
        public System.Random Rng { get; }
        /// <summary>Falso sul server headless: niente renderer né mesh.</summary>
        public bool Graphics { get; }
        public Transform Root { get; }
        public ArtLibrary Art { get; }

        public int ShapeCount { get; private set; }
        public int ModelCount { get; private set; }
        public int ColliderCount { get; private set; }
        public int LightCount { get; private set; }
        public IReadOnlyList<AreaAnchor> Anchors => _anchors;
        public IReadOnlyList<PreviewShot> Shots => _shots;

        internal MeshCombiner Combiner => _combiner;
        internal List<UnityEngine.Object> Owned => _owned;
        internal ulong OperationsHash => _hash.Value;

        readonly MeshCombiner _combiner;
        readonly Transform _colliderRoot, _anchorRoot, _lightRoot;
        readonly Dictionary<long, GameObject> _colliderHolders = new Dictionary<long, GameObject>();
        readonly List<AreaAnchor> _anchors = new List<AreaAnchor>();
        readonly HashSet<string> _anchorIds = new HashSet<string>(StringComparer.Ordinal);
        readonly List<PreviewShot> _shots = new List<PreviewShot>();
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly Dictionary<Material, int> _flatIndex = new Dictionary<Material, int>();
        WorldHash _hash;
        Material _matte, _gloss, _glow, _clear;

        internal BuildContext(AreaId area, int seed, Transform root, ArtLibrary art, bool graphics, float chunkSize)
        {
            Area = area;
            AreaKey = area.ToString().ToLowerInvariant();
            Seed = seed;
            Rng = new System.Random(seed);
            Graphics = graphics;
            Root = root;
            Art = art;
            _combiner = new MeshCombiner(chunkSize);
            _colliderRoot = Child("Colliders");
            _anchorRoot = Child("Anchors");
            _lightRoot = graphics ? Child("Lights") : null;
            _hash.Add(area.ToString());
            _hash.Add(seed);
            if (graphics) CreateMaterials();
        }

        Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(Root, false);
            return t;
        }

        void CreateMaterials()
        {
            var tex = WorldPalette.CreateTexture(Art != null ? Art.flatColors : null);
            _owned.Add(tex);
            _matte = PaletteMaterial(Art != null ? Art.paletteMatte : null, "World_Matte", tex, false);
            _gloss = PaletteMaterial(Art != null ? Art.paletteGloss : null, "World_Gloss", tex, false);
            _glow = PaletteMaterial(Art != null ? Art.paletteGlow : null, "World_Glow", tex, true);
            _clear = Art != null && Art.paletteClear != null ? PaletteMaterial(Art.paletteClear, "World_Clear", tex, false) : _gloss;
        }

        Material PaletteMaterial(Material template, string name, Texture2D tex, bool emissive)
        {
            var m = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = name;
            m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (emissive) m.SetTexture("_EmissionMap", tex);
            _owned.Add(m);
            return m;
        }

        // ------------------------------------------------------------------ numeri casuali

        /// <summary>Numero casuale in [min, max).</summary>
        public float Range(float min, float max) => min + (float)Rng.NextDouble() * (max - min);

        /// <summary>Intero casuale in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive) => Rng.Next(min, maxExclusive);

        public bool Chance(float probability) => Rng.NextDouble() < probability;

        public T Pick<T>(IReadOnlyList<T> items) => items[Rng.Next(items.Count)];

        // ------------------------------------------------------------------ forme

        /// <summary>Scatola con un colore della tavolozza. <paramref name="yaw"/> in gradi attorno all'asse verticale.</summary>
        public void Box(Vector3 center, Vector3 size, Pal color, float yaw = 0f, bool solid = true, BoxFaces faces = BoxFaces.NoBottom)
            => Box(center, size, color, Quaternion.Euler(0f, yaw, 0f), solid, faces);

        /// <summary>Scatola con rotazione libera.</summary>
        public void Box(Vector3 center, Vector3 size, Pal color, Quaternion rotation, bool solid = true, BoxFaces faces = BoxFaces.NoBottom)
        {
            _hash.Add(1); _hash.Add(center); _hash.Add(size); _hash.Add(rotation);
            _hash.Add((int)color); _hash.Add(solid); _hash.Add((int)faces);
            ShapeCount++;
            if (Graphics)
                _combiner.GetBucket(Material(color), center, Mathf.Max(size.x, size.z))
                    .AddBox(center, size, rotation, Uv(color), faces);
            if (solid) AddCollider(center, size, rotation);
        }

        /// <summary>Scatola allineata agli assi, dai due angoli opposti.</summary>
        public void BoxMinMax(Vector3 min, Vector3 max, Pal color, bool solid = true, BoxFaces faces = BoxFaces.NoBottom)
            => Box((min + max) * 0.5f, max - min, color, Quaternion.identity, solid, faces);

        /// <summary>
        /// Prisma verticale a N lati (cilindro low poly). <paramref name="baseCenter"/> = centro del fondo.
        /// Collider: una scatola (due per prismi larghi a 8+ lati, che insieme fanno un ottagono).
        /// </summary>
        public void Prism(Vector3 baseCenter, float radius, float height, int sides, Pal color, bool solid = true,
            float startDeg = 0f, bool top = true, bool bottom = false)
        {
            _hash.Add(2); _hash.Add(baseCenter); _hash.Add(radius); _hash.Add(height); _hash.Add(sides);
            _hash.Add((int)color); _hash.Add(solid); _hash.Add(startDeg); _hash.Add(top); _hash.Add(bottom);
            ShapeCount++;
            if (Graphics)
                _combiner.GetBucket(Material(color), baseCenter, radius * 2f)
                    .AddPrism(baseCenter, radius, height, sides, startDeg, Uv(color), top, bottom);
            if (!solid) return;
            sides = Mathf.Max(3, sides);
            float apothem = radius * Mathf.Cos(Mathf.PI / sides);
            float mid = startDeg + 180f / sides;
            var c = baseCenter + Vector3.up * (height * 0.5f);
            var size = new Vector3(apothem * 2f, height, apothem * 2f);
            AddCollider(c, size, Quaternion.Euler(0f, -mid, 0f));
            if (sides >= 8 && radius >= 1.2f) AddCollider(c, size, Quaternion.Euler(0f, -mid - 45f, 0f));
        }

        /// <summary>Collider invisibile (muri di confine, blocchi).</summary>
        public void Collider(Vector3 center, Vector3 size, float yaw = 0f)
        {
            _hash.Add(3); _hash.Add(center); _hash.Add(size); _hash.Add(yaw);
            AddCollider(center, size, Quaternion.Euler(0f, yaw, 0f));
        }

        // ------------------------------------------------------------------ modelli

        /// <summary>
        /// Mette un modello della libreria come arredo fermo (mesh unite). Il punto è dove il modello appoggia.
        /// <paramref name="paint"/> ≥ 0 cambia il colore della carrozzeria delle auto civili (indice in ArtLibrary.CarPaints).
        /// </summary>
        public void Model(string id, Vector3 position, float yaw, int paint = -1, bool solid = true, float scaleMultiplier = 1f)
        {
            var model = Art.Get(id);
            float scale = model.scale * scaleMultiplier;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            _hash.Add(4); _hash.Add(id); _hash.Add(position); _hash.Add(yaw); _hash.Add(paint); _hash.Add(solid); _hash.Add(scale);
            ModelCount++;
            // Appoggia il modello a terra: alcuni file (Nature Kit) hanno il fondo un po' sotto lo zero.
            position.y -= model.bounds.min.y * scale;

            if (Graphics) AddModelGeometry(model, position, rot, scale, paint);

            if (!solid || model.collider == ArtCollider.None) return;
            var b = new Bounds(model.bounds.center * scale, model.bounds.size * scale);
            if (model.collider == ArtCollider.Trunk)
            {
                float trunk = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.18f, 0.2f, 0.6f);
                float top = b.max.y - b.min.y;
                AddCollider(position + Vector3.up * (b.min.y + top * 0.5f), new Vector3(trunk, top, trunk), rot);
            }
            else
            {
                AddCollider(position + rot * b.center, b.size, rot);
            }
        }

        void AddModelGeometry(ArtModel model, Vector3 position, Quaternion rot, float scale, int paint)
        {
            var trs = Matrix4x4.TRS(position, rot, Vector3.one * scale);
            var kitMat = Art.KitMaterial(model.kit);
            Rect region = default;
            Vector2 shift = Vector2.zero;
            if (paint >= 0 && model.Recolorable && kitMat != null)
            {
                int target = ArtLibrary.CarPaints[paint % ArtLibrary.CarPaints.Length];
                region = PairRect(model.bodyPair);
                var to = PairRect(target);
                shift = to.position - region.position;
            }
            float extent = Mathf.Max(model.bounds.size.x, model.bounds.size.z) * scale;
            foreach (var part in Art.GetParts(model))
            {
                var src = _combiner.Source(part.Mesh);
                var m = trs * part.Local;
                for (int s = 0; s < src.SubIndices.Length; s++)
                {
                    if (kitMat != null)
                    {
                        _combiner.GetBucket(kitMat, position, extent).AddMesh(src, s, m, null, region, shift);
                    }
                    else
                    {
                        // Modelli a colori pieni (Nature Kit): il colore diventa un pixel della tavolozza.
                        var src_mat = part.Materials.Length > 0 ? part.Materials[Mathf.Min(s, part.Materials.Length - 1)] : null;
                        int index = FlatPaletteIndex(src_mat);
                        _combiner.GetBucket(_matte, position, extent).AddMesh(src, s, m, WorldPalette.Uv(index), default, Vector2.zero);
                    }
                }
            }
        }

        int FlatPaletteIndex(Material m)
        {
            if (m == null) return (int)Pal.Grass;
            if (_flatIndex.TryGetValue(m, out int idx)) return idx;
            var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            int k = Art.FlatColorIndex(c);
            idx = k >= 0 && (int)Pal.Count + k < WorldPalette.MaxColors ? (int)Pal.Count + k : (int)Pal.Grass;
            if (k < 0) Debug.LogWarning($"[World] Colore {c} del materiale {m.name} non è nella libreria: uso il verde.");
            _flatIndex[m] = idx;
            return idx;
        }

        /// <summary>Rettangolo uv di una coppia di colori della colormap Kenney (riga * 8 + coppia).</summary>
        public static Rect PairRect(int pair)
        {
            int row = pair / 8, col = pair % 8;
            return new Rect(col / 8f, 1f - (row + 1) / 4f, 1f / 8f, 1f / 4f);
        }

        // ------------------------------------------------------------------ punti speciali, luci, inquadrature

        /// <summary>
        /// Crea un punto speciale. <paramref name="name"/> è senza area: "spawn.0" diventa "downtown.spawn.0".
        /// La rotazione (yaw) dice dove guarda chi appare lì.
        /// </summary>
        public AreaAnchor Anchor(string name, AnchorKind kind, Vector3 position, float yaw, float radius = 2f, string data = null)
        {
            string id = AreaKey + "." + name;
            if (!_anchorIds.Add(id)) throw new InvalidOperationException($"[World] Punto speciale duplicato: {id}");
            _hash.Add(5); _hash.Add(id); _hash.Add((int)kind); _hash.Add(position); _hash.Add(yaw); _hash.Add(radius); _hash.Add(data);

            var go = new GameObject(id);
            go.SetActive(false); // l'Id deve esserci prima di OnEnable (registrazione in AreaAnchors)
            go.transform.SetParent(_anchorRoot, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var a = go.AddComponent<AreaAnchor>();
            a.Id = id;
            a.Kind = kind;
            a.Radius = radius;
            a.Data = data;
            go.SetActive(true);
            _anchors.Add(a);
            return a;
        }

        /// <summary>Luce puntiforme senza ombre (solo con la grafica).</summary>
        public void Light(Vector3 position, Color color, float range, float intensity)
        {
            _hash.Add(6); _hash.Add(position); _hash.Add(range);
            LightCount++;
            if (!Graphics) return;
            var go = new GameObject("Light");
            go.transform.SetParent(_lightRoot, false);
            go.transform.position = position;
            var l = go.AddComponent<UnityEngine.Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }

        /// <summary>Inquadratura per il modo anteprima.</summary>
        public void PreviewShot(string name, Vector3 cameraPosition, Vector3 lookAt) => _shots.Add(new PreviewShot(name, cameraPosition, lookAt));

        // ------------------------------------------------------------------ scritte

        /// <summary>
        /// Scritta a "pixel" (font 5x7 fatto di scatole). <paramref name="center"/> = centro della scritta sul piano
        /// dell'insegna; le lettere sporgono di <paramref name="depth"/> verso <paramref name="yaw"/> (dove guarda la scritta).
        /// Righe separate da '\n'. Ritorna la larghezza in metri.
        /// </summary>
        public float Text(string text, Vector3 center, float yaw, float pixel, Pal color, float depth = 0.08f, TextAlign align = TextAlign.Center)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 read = rot * Vector3.left; // per chi guarda l'insegna, la lettura va verso la sua destra
            Vector3 outDir = rot * Vector3.forward;
            var lines = text.Split('\n');
            float lineH = PixelFont.LineHeight * pixel;
            float totalH = lines.Length * lineH - (PixelFont.LineHeight - PixelFont.GlyphHeight) * pixel;
            float maxW = 0f;
            foreach (var line in lines) maxW = Mathf.Max(maxW, PixelFont.Width(line) * pixel);

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                float w = PixelFont.Width(line) * pixel;
                float x0 = align switch
                {
                    TextAlign.Left => -maxW * 0.5f,
                    TextAlign.Right => maxW * 0.5f - w,
                    _ => -w * 0.5f,
                };
                float top = totalH * 0.5f - li * lineH;
                foreach (var run in PixelFont.Runs(line))
                {
                    float cx = x0 + (run.X + run.Length * 0.5f) * pixel;
                    float cy = top - (run.Row + 0.5f) * pixel;
                    var c = center + read * cx + Vector3.up * cy + outDir * (depth * 0.5f);
                    Box(c, new Vector3(run.Length * pixel, pixel, depth), color, rot, false, BoxFaces.All & ~BoxFaces.NegZ);
                }
            }
            return maxW;
        }

        // ------------------------------------------------------------------ interni

        Material Material(Pal color) => WorldPalette.SurfaceOf(color) switch
        {
            PalSurface.Gloss => _gloss,
            PalSurface.Glow => _glow,
            PalSurface.Clear => _clear,
            _ => _matte,
        };

        static Vector2 Uv(Pal color) => WorldPalette.Uv((int)color);

        void AddCollider(Vector3 center, Vector3 size, Quaternion rot)
        {
            ColliderCount++;
            Vector3 right = rot * Vector3.right;
            Vector3 up = rot * Vector3.up;
            bool upright = up.y > 0.9999f;
            if (upright && Mathf.Abs(right.x) > 0.9999f)
            {
                AddHolderCollider(center, size);
                return;
            }
            if (upright && Mathf.Abs(right.z) > 0.9999f)
            {
                AddHolderCollider(center, new Vector3(size.z, size.y, size.x));
                return;
            }
            var go = new GameObject("Collider");
            go.transform.SetParent(_colliderRoot, false);
            go.transform.SetPositionAndRotation(center, rot);
            go.AddComponent<BoxCollider>().size = size;
        }

        // Collider allineati agli assi: tanti BoxCollider su un oggetto per chunk (meno oggetti da creare).
        void AddHolderCollider(Vector3 center, Vector3 size)
        {
            int cx = Mathf.FloorToInt(center.x / _combiner.ChunkSize);
            int cz = Mathf.FloorToInt(center.z / _combiner.ChunkSize);
            long key = ((long)cx << 32) ^ (uint)cz;
            if (!_colliderHolders.TryGetValue(key, out var holder))
            {
                holder = new GameObject($"Colliders_{cx}_{cz}");
                holder.transform.SetParent(_colliderRoot, false);
                _colliderHolders.Add(key, holder);
            }
            var bc = holder.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }
    }
}
