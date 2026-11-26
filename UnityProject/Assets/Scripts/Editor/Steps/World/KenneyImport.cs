using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Underworld.EditorTools
{
    /// <summary>I tre kit Kenney copiati dal vault in Assets/Art/Kenney/.</summary>
    public enum KenneyKit { None, CarKit, FactoryKit, NatureKit }

    /// <summary>Percorsi dei kit Kenney nel progetto.</summary>
    public static class KenneyPaths
    {
        public const string Root = "Assets/Art/Kenney/";
        public const string CarKit = Root + "CarKit";
        public const string FactoryKit = Root + "FactoryKit";
        public const string NatureKit = Root + "NatureKit";

        /// <summary>Kit a cui appartiene un file (dal percorso).</summary>
        public static KenneyKit KitOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith(Root)) return KenneyKit.None;
            if (assetPath.StartsWith(CarKit + "/")) return KenneyKit.CarKit;
            if (assetPath.StartsWith(FactoryKit + "/")) return KenneyKit.FactoryKit;
            if (assetPath.StartsWith(NatureKit + "/")) return KenneyKit.NatureKit;
            return KenneyKit.None;
        }

        /// <summary>Texture "colormap" del kit (Car Kit e Factory Kit). Il Nature Kit usa colori pieni: null.</summary>
        public static string ColormapPath(KenneyKit kit) => kit switch
        {
            KenneyKit.CarKit => CarKit + "/Textures/colormap.png",
            KenneyKit.FactoryKit => FactoryKit + "/Textures/colormap.png",
            _ => null,
        };
    }

    /// <summary>
    /// Importa i modelli Kenney in modo corretto per URP.
    /// - Niente animazioni, camere, luci, blend shape.
    /// - Mesh leggibili (Read/Write): il MeshCombiner del mondo legge i vertici a runtime.
    /// - Scala del file (1 unità Kenney = 1 m): la scala reale la decide la libreria (ArtLibrary) dalle misure.
    /// - Materiali URP Lit opachi: Car Kit e Factory Kit con la loro texture "colormap", Nature Kit con il colore del file.
    /// Gira dopo il preprocessore FBX di URP (ordine -980), così le nostre impostazioni vincono.
    /// </summary>
    public sealed class KenneyModelPostprocessor : AssetPostprocessor
    {
        // Cambia il numero se cambi le regole: Unity reimporta i modelli.
        const uint Version = 5;

        public override uint GetVersion() => Version;
        public override int GetPostprocessOrder() => 100;

        void OnPreprocessModel()
        {
            if (KenneyPaths.KitOf(assetPath) == KenneyKit.None) return;
            var mi = (ModelImporter)assetImporter;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.importConstraints = false;
            mi.isReadable = true;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
            mi.useFileScale = true;
            mi.globalScale = 1f;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.sortHierarchyByName = true;
        }

        void OnPreprocessTexture()
        {
            if (KenneyPaths.KitOf(assetPath) == KenneyKit.None) return;
            var ti = (TextureImporter)assetImporter;
            // Atlante di colori: niente mipmap (i colori vicini si mescolerebbero da lontano),
            // niente compressione (blocchi puliti), bordi bloccati.
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.isReadable = false;
            ti.alphaSource = TextureImporterAlphaSource.None;
        }

        void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            var kit = KenneyPaths.KitOf(assetPath);
            if (kit == KenneyKit.None) return;

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null) material.shader = lit;
            KenneyMaterials.MakeOpaque(material);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", kit == KenneyKit.NatureKit ? 0.08f : 0.3f);
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");

            string colormap = KenneyPaths.ColormapPath(kit);
            if (colormap != null)
            {
                context.DependsOnSourceAsset(colormap);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(colormap);
                material.SetTexture("_BaseMap", tex);
                material.SetTexture("_MainTex", tex);
                material.SetColor("_BaseColor", Color.white);
                material.SetColor("_Color", Color.white);
            }
            else
            {
                // Nature Kit: colore pieno. I valori del file sono già sRGB (palette Kenney, es. foglie #29C9AB):
                // URP li schiarisce con una conversione in più, quindi li rileggiamo dal file così come sono.
                Color c = Color.white;
                if (description.TryGetProperty("DiffuseColor", out Vector4 diffuse))
                    c = new Color(diffuse.x, diffuse.y, diffuse.z, 1f);
                else if (material.HasProperty("_BaseColor"))
                    c = material.GetColor("_BaseColor");
                c.a = 1f;
                material.SetColor("_BaseColor", c);
                material.SetColor("_Color", c);
                material.SetTexture("_BaseMap", null);
                material.SetTexture("_MainTex", null);
            }
        }
    }

    /// <summary>Aiuti per i materiali URP Lit creati dal codice dell'editor.</summary>
    public static class KenneyMaterials
    {
        /// <summary>Superficie opaca, senza alpha test né blend (come "Opaque" nell'inspector di URP).</summary>
        public static void MakeOpaque(Material m)
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.SetOverrideTag("RenderType", "Opaque");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
    }
}
