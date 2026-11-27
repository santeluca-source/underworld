using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Underworld.World;
using UnityEditor;
using UnityEngine;

namespace Underworld.EditorTools
{
    /// <summary>Come si misura un modello per portarlo alla misura reale.</summary>
    public enum ArtMeasure
    {
        /// <summary>Lato orizzontale più lungo (auto: lunghezza).</summary>
        Length,
        /// <summary>Altezza (alberi, coni, pali).</summary>
        Height,
    }

    /// <summary>Una voce da mettere nella libreria: id stabile, file, misura reale.</summary>
    public sealed class ArtEntry
    {
        public string Id;
        public string Path;
        public ArtMeasure Measure;
        public float Meters;
        public ArtCollider Collider = ArtCollider.Bounds;
        /// <summary>Auto civili: si può cambiare il colore della carrozzeria.</summary>
        public bool Recolor;
    }

    /// <summary>Elenco delle voci della libreria, riempito dai contributori.</summary>
    public sealed class ArtLibraryDefinitions
    {
        readonly List<ArtEntry> _entries = new List<ArtEntry>();
        public IReadOnlyList<ArtEntry> Entries => _entries;

        public ArtEntry Add(string id, string path, ArtMeasure measure, float meters, ArtCollider collider = ArtCollider.Bounds, bool recolor = false)
        {
            if (_entries.Any(e => e.Id == id)) throw new InvalidOperationException($"Id doppio nella libreria: {id}");
            var e = new ArtEntry { Id = id, Path = path, Measure = measure, Meters = meters, Collider = collider, Recolor = recolor };
            _entries.Add(e);
            return e;
        }
    }

    /// <summary>
    /// Aggiunge modelli alla libreria dell'arte. Ogni modulo può avere il suo contributore (es. Industrial):
    /// il passo "Arte del mondo" li trova da solo (TypeCache). Nessun file centrale da modificare.
    /// </summary>
    public interface IArtLibraryContributor
    {
        void Contribute(ArtLibraryDefinitions defs);
    }

    /// <summary>
    /// Passo di setup "Arte del mondo":
    /// 1. materiali condivisi (tavolozza opaca, lucida, luminosa; colormap Car Kit e Factory Kit);
    /// 2. libreria Resources/World/ArtLibrary con misura, scala reale e collider di ogni modello;
    /// 3. controlli: niente materiali rosa, niente modelli mancanti.
    /// </summary>
    public sealed class WorldArtStep : IProjectSetupStep
    {
        public const string MaterialFolder = "Assets/Art/World/Materials";
        public const string LibraryPath = "Assets/Resources/World/ArtLibrary.asset";

        public int Order => 150;
        public string Name => "Arte del mondo (Kenney + libreria)";

        public void Run(SetupContext ctx)
        {
            SetupContext.EnsureFolder(MaterialFolder);
            SetupContext.EnsureFolder("Assets/Resources/World");
            FixColormapImports();

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new InvalidOperationException("Shader URP Lit non trovato.");

            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<ArtLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            lib.paletteMatte = LitMaterial("World_PaletteMatte", lit, null, 0.12f, false);
            lib.paletteGloss = LitMaterial("World_PaletteGloss", lit, null, 0.82f, false);
            lib.paletteGlow = LitMaterial("World_PaletteGlow", lit, null, 0.3f, true);
            lib.paletteClear = LitMaterial("World_PaletteClear", lit, null, 0.9f, false);
            KenneyMaterials.MakeTransparent(lib.paletteClear, 0.32f);
            lib.carKit = LitMaterial("Kenney_CarKit", lit, AssetDatabase.LoadAssetAtPath<Texture2D>(KenneyPaths.ColormapPath(KenneyKit.CarKit)), 0.35f, false);
            lib.factoryKit = LitMaterial("Kenney_FactoryKit", lit, AssetDatabase.LoadAssetAtPath<Texture2D>(KenneyPaths.ColormapPath(KenneyKit.FactoryKit)), 0.3f, false);

            var defs = new ArtLibraryDefinitions();
            foreach (var t in TypeCache.GetTypesDerivedFrom<IArtLibraryContributor>()
                         .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                         .OrderBy(t => t.FullName, StringComparer.Ordinal))
                ((IArtLibraryContributor)Activator.CreateInstance(t)).Contribute(defs);

            var carColors = ReadColormap(KenneyPaths.ColormapPath(KenneyKit.CarKit));
            var report = new StringBuilder("[UNDERWORLD] Libreria arte:\n");
            var models = new List<ArtModel>();
            var flat = new List<Color>();
            foreach (var e in defs.Entries)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.Path);
                if (prefab == null) throw new InvalidOperationException($"Modello mancante: {e.Path} (id {e.Id})");
                var kit = KitOf(e.Path);
                var bounds = Measure(prefab);
                float measured = e.Measure == ArtMeasure.Height ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.z);
                if (measured <= 0.0001f) throw new InvalidOperationException($"Modello senza misura: {e.Path}");
                var m = new ArtModel
                {
                    id = e.Id,
                    prefab = prefab,
                    kit = kit,
                    bounds = bounds,
                    scale = e.Meters / measured,
                    collider = e.Collider,
                    bodyPair = e.Recolor && kit == ArtKit.CarKit ? FindBodyPair(prefab, carColors) : -1,
                };
                CheckMaterials(prefab, e.Id);
                if (kit == ArtKit.NatureKit) CollectFlatColors(prefab, flat);
                models.Add(m);
                var real = m.RealSize;
                report.AppendLine($"  {e.Id,-24} {Path.GetFileName(e.Path),-28} misura={measured:0.00} scala={m.scale:0.000} reale=({real.x:0.00} x {real.y:0.00} x {real.z:0.00}) m"
                                  + (m.bodyPair >= 0 ? $" carrozzeria=r{m.bodyPair / 8}p{m.bodyPair % 8}" : ""));
            }
            if (flat.Count + (int)Pal.Count > WorldPalette.MaxColors)
                throw new InvalidOperationException($"Troppi colori nella tavolozza: {flat.Count + (int)Pal.Count}");

            lib.models = models;
            lib.flatColors = flat;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            report.AppendLine($"  modelli={models.Count}, colori pieni={flat.Count}");
            Debug.Log(report.ToString());
        }

        // ------------------------------------------------------------------ materiali

        static Material LitMaterial(string name, Shader lit, Texture2D tex, float smoothness, bool emissive)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(lit) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = lit;
            KenneyMaterials.MakeOpaque(m);
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_Color", Color.white);
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_EnvironmentReflections", 1f);
            m.SetFloat("_SpecularHighlights", 1f);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(1.6f, 1.6f, 1.6f, 1f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            m.enableInstancing = false; // le mesh sono già unite: serve l'SRP Batcher, non l'instancing
            EditorUtility.SetDirty(m);
            return m;
        }

        static void FixColormapImports()
        {
            foreach (var kit in new[] { KenneyKit.CarKit, KenneyKit.FactoryKit })
            {
                string p = KenneyPaths.ColormapPath(kit);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) throw new InvalidOperationException($"Texture mancante: {p}");
                if (ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    ti.mipmapEnabled = false;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.SaveAndReimport();
                }
            }
        }

        // ------------------------------------------------------------------ misure

        static ArtKit KitOf(string path) => KenneyPaths.KitOf(path) switch
        {
            KenneyKit.CarKit => ArtKit.CarKit,
            KenneyKit.FactoryKit => ArtKit.FactoryKit,
            KenneyKit.NatureKit => ArtKit.NatureKit,
            _ => ArtKit.Code,
        };

        /// <summary>Riquadro di tutti i vertici del modello (gerarchia inclusa), nello spazio del prefab.</summary>
        public static Bounds Measure(GameObject prefab)
        {
            bool has = false;
            var b = new Bounds();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    if (!has) { b = new Bounds(w, Vector3.zero); has = true; }
                    else b.Encapsulate(w);
                }
            }
            return b;
        }

        static void CheckMaterials(GameObject prefab, string id)
        {
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) throw new InvalidOperationException($"{id}: materiale mancante");
                    if (mat.shader == null || mat.shader.name != "Universal Render Pipeline/Lit")
                        throw new InvalidOperationException($"{id}: materiale {mat.name} con shader {mat.shader?.name} (serve URP Lit)");
                    var kit = KenneyPaths.KitOf(AssetDatabase.GetAssetPath(prefab));
                    if (KenneyPaths.ColormapPath(kit) != null && mat.GetTexture("_BaseMap") == null)
                        throw new InvalidOperationException($"{id}: materiale {mat.name} senza colormap (sarebbe bianco)");
                }
        }

        static void CollectFlatColors(GameObject prefab, List<Color> flat)
        {
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var mat in r.sharedMaterials)
                {
                    var c = mat.GetColor("_BaseColor");
                    bool known = flat.Any(f => Mathf.Abs(f.r - c.r) < 0.004f && Mathf.Abs(f.g - c.g) < 0.004f && Mathf.Abs(f.b - c.b) < 0.004f);
                    if (!known) flat.Add(new Color(c.r, c.g, c.b, 1f));
                }
        }

        // ------------------------------------------------------------------ colore della carrozzeria

        /// <summary>Colori delle 32 coppie della colormap (riga * 8 + coppia), letti dal PNG.</summary>
        static Color[] ReadColormap(string path)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(path));
            var colors = new Color[32];
            for (int row = 0; row < 4; row++)
                for (int pair = 0; pair < 8; pair++)
                {
                    int x = pair * tex.width / 8 + tex.width / 32;
                    int y = tex.height - 1 - (row * tex.height / 4 + tex.height / 8);
                    colors[row * 8 + pair] = tex.GetPixel(x, y);
                }
            UnityEngine.Object.DestroyImmediate(tex);
            return colors;
        }

        /// <summary>
        /// Coppia di colori della carrozzeria: la coppia "colorata" (satura) con più triangoli nella mesh "body".
        /// </summary>
        static int FindBodyPair(GameObject prefab, Color[] pairColors)
        {
            var counts = new int[32];
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.name.StartsWith("body", StringComparison.OrdinalIgnoreCase)) continue;
                var mesh = mf.sharedMesh;
                var uv = mesh.uv;
                var tris = mesh.triangles;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    var c = (uv[tris[t]] + uv[tris[t + 1]] + uv[tris[t + 2]]) / 3f;
                    int pair = Mathf.Clamp(Mathf.FloorToInt(c.x * 8f), 0, 7);
                    int row = Mathf.Clamp(Mathf.FloorToInt((1f - c.y) * 4f), 0, 3);
                    counts[row * 8 + pair]++;
                }
            }
            int best = -1;
            for (int i = 0; i < 32; i++)
            {
                Color.RGBToHSV(pairColors[i], out _, out float s, out float v);
                if (s < 0.35f || v < 0.3f || counts[i] == 0) continue;
                if (best < 0 || counts[i] > counts[best]) best = i;
            }
            return best;
        }
    }

    /// <summary>Modelli Kenney usati dal mondo (Downtown e aree future).</summary>
    public sealed class KenneyWorldArt : IArtLibraryContributor
    {
        const string Car = KenneyPaths.CarKit + "/";
        const string Factory = KenneyPaths.FactoryKit + "/";
        const string Nature = KenneyPaths.NatureKit + "/";

        public void Contribute(ArtLibraryDefinitions d)
        {
            // Auto: lunghezze reali (una berlina è circa 4,3 m). Le civili cambiano colore.
            d.Add("car.sedan", Car + "sedan.fbx", ArtMeasure.Length, 4.3f, recolor: true);
            d.Add("car.sedan_sports", Car + "sedan-sports.fbx", ArtMeasure.Length, 4.4f, recolor: true);
            d.Add("car.hatchback", Car + "hatchback-sports.fbx", ArtMeasure.Length, 4.0f, recolor: true);
            d.Add("car.suv", Car + "suv.fbx", ArtMeasure.Length, 4.6f, recolor: true);
            d.Add("car.suv_luxury", Car + "suv-luxury.fbx", ArtMeasure.Length, 4.8f, recolor: true);
            d.Add("car.van", Car + "van.fbx", ArtMeasure.Length, 4.9f, recolor: true);
            d.Add("car.taxi", Car + "taxi.fbx", ArtMeasure.Length, 4.4f);
            d.Add("car.police", Car + "police.fbx", ArtMeasure.Length, 4.6f);
            d.Add("car.ambulance", Car + "ambulance.fbx", ArtMeasure.Length, 5.8f);
            d.Add("car.firetruck", Car + "firetruck.fbx", ArtMeasure.Length, 8.0f);
            d.Add("car.delivery", Car + "delivery.fbx", ArtMeasure.Length, 5.6f);
            d.Add("car.delivery_flat", Car + "delivery-flat.fbx", ArtMeasure.Length, 5.6f);
            d.Add("car.truck", Car + "truck.fbx", ArtMeasure.Length, 6.8f);
            d.Add("car.truck_flat", Car + "truck-flat.fbx", ArtMeasure.Length, 6.8f);
            d.Add("car.garbage_truck", Car + "garbage-truck.fbx", ArtMeasure.Length, 7.6f);

            // Arredo stradale (Car Kit).
            d.Add("prop.cone", Car + "cone.fbx", ArtMeasure.Height, 0.7f);
            d.Add("prop.cone_flat", Car + "cone-flat.fbx", ArtMeasure.Length, 0.55f);
            d.Add("prop.box", Car + "box.fbx", ArtMeasure.Height, 0.55f);
            d.Add("prop.tire", Car + "debris-tire.fbx", ArtMeasure.Length, 0.7f);

            // Factory Kit: casse e segnali (vicoli, cantieri).
            d.Add("prop.crate_small", Factory + "box-small.fbx", ArtMeasure.Height, 0.55f);
            d.Add("prop.crate_large", Factory + "box-large.fbx", ArtMeasure.Height, 1.0f);
            d.Add("prop.crate_long", Factory + "box-long.fbx", ArtMeasure.Length, 1.6f);
            d.Add("prop.crate_wide", Factory + "box-wide.fbx", ArtMeasure.Length, 1.4f);
            d.Add("prop.warning_post", Factory + "warning-traffic.fbx", ArtMeasure.Height, 1.2f);
            d.Add("prop.warning_sign", Factory + "warning-orange.fbx", ArtMeasure.Height, 1.3f);

            // Nature Kit: alberi (collider solo sul tronco), cespugli e fiori (senza collider).
            d.Add("nature.tree_oak", Nature + "tree_oak.fbx", ArtMeasure.Height, 7.0f, ArtCollider.Trunk);
            d.Add("nature.tree_oak_dark", Nature + "tree_oak_dark.fbx", ArtMeasure.Height, 7.0f, ArtCollider.Trunk);
            d.Add("nature.tree_default", Nature + "tree_default.fbx", ArtMeasure.Height, 7.5f, ArtCollider.Trunk);
            d.Add("nature.tree_default_dark", Nature + "tree_default_dark.fbx", ArtMeasure.Height, 7.5f, ArtCollider.Trunk);
            d.Add("nature.tree_detailed", Nature + "tree_detailed.fbx", ArtMeasure.Height, 7.0f, ArtCollider.Trunk);
            d.Add("nature.tree_fat", Nature + "tree_fat.fbx", ArtMeasure.Height, 6.5f, ArtCollider.Trunk);
            d.Add("nature.tree_small", Nature + "tree_small.fbx", ArtMeasure.Height, 4.5f, ArtCollider.Trunk);
            d.Add("nature.tree_tall", Nature + "tree_tall.fbx", ArtMeasure.Height, 9.0f, ArtCollider.Trunk);
            d.Add("nature.tree_cone", Nature + "tree_cone.fbx", ArtMeasure.Height, 6.5f, ArtCollider.Trunk);
            d.Add("nature.tree_plateau", Nature + "tree_plateau.fbx", ArtMeasure.Height, 6.0f, ArtCollider.Trunk);
            d.Add("nature.tree_oak_fall", Nature + "tree_oak_fall.fbx", ArtMeasure.Height, 7.0f, ArtCollider.Trunk);
            d.Add("nature.bush", Nature + "plant_bush.fbx", ArtMeasure.Height, 0.8f, ArtCollider.None);
            d.Add("nature.bush_large", Nature + "plant_bushLarge.fbx", ArtMeasure.Height, 1.2f, ArtCollider.None);
            d.Add("nature.bush_detailed", Nature + "plant_bushDetailed.fbx", ArtMeasure.Height, 1.0f, ArtCollider.None);
            d.Add("nature.flower_red", Nature + "flower_redA.fbx", ArtMeasure.Height, 0.45f, ArtCollider.None);
            d.Add("nature.flower_yellow", Nature + "flower_yellowA.fbx", ArtMeasure.Height, 0.45f, ArtCollider.None);
            d.Add("nature.flower_purple", Nature + "flower_purpleA.fbx", ArtMeasure.Height, 0.45f, ArtCollider.None);
            d.Add("nature.grass", Nature + "grass.fbx", ArtMeasure.Height, 0.35f, ArtCollider.None);
            d.Add("nature.rock_small", Nature + "rock_smallA.fbx", ArtMeasure.Length, 0.6f, ArtCollider.None);
            d.Add("nature.pot_large", Nature + "pot_large.fbx", ArtMeasure.Height, 0.9f);
            d.Add("nature.pot_small", Nature + "pot_small.fbx", ArtMeasure.Height, 0.6f);
            d.Add("nature.statue_obelisk", Nature + "statue_obelisk.fbx", ArtMeasure.Height, 4.0f);
        }
    }
}
