using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Underworld.UI;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Asset della UI: PanelSettings (in Resources, lo carica UiRoot), tema e materiali dello sfondo del menu.
    /// I fogli USS e il tema (.tss) sono file scritti a mano in Assets/UI: qui si controllano e si collegano.
    /// </summary>
    public sealed class UiAssetsStep : IProjectSetupStep
    {
        public const string ThemePath = "Assets/UI/Theme/UnderworldTheme.tss";
        public const string ResourcesFolder = "Assets/UI/Resources/Underworld";
        public const string PanelSettingsPath = ResourcesFolder + "/UiPanelSettings.asset";
        public const string MaterialsFolder = "Assets/UI/Backdrop/Materials";

        public const string BuildingsShader = "Underworld/UI/SkylineBuildings";
        public const string GlowShader = "Underworld/UI/SkylineGlow";
        public const string GroundShader = "Underworld/UI/SkylineGround";
        public const string SkyShader = "Underworld/UI/SkylineSky";

        public int Order => 150;
        public string Name => "UI: tema, PanelSettings, materiali dello sfondo";

        public void Run(SetupContext ctx)
        {
            // Il percorso in Resources deve corrispondere a quello che legge UiRoot.
            if (ResourcesFolder.Substring(ResourcesFolder.IndexOf("Resources/") + 10) + "/UiPanelSettings" != UiRoot.PanelSettingsResource)
                throw new System.Exception("Percorso del PanelSettings diverso da UiRoot.PanelSettingsResource");

            AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceUpdate);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null) throw new System.Exception("Tema non trovato: " + ThemePath);

            SetupContext.EnsureFolder(ResourcesFolder);
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            }
            ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.sortingOrder = 100;
            ps.clearColor = false;
            ps.clearDepthStencil = true;
            EditorUtility.SetDirty(ps);

            SetupContext.EnsureFolder(MaterialsFolder);
            EnsureMaterial("SkylineBuildings", BuildingsShader);
            EnsureMaterial("SkylineGlow", GlowShader);
            EnsureMaterial("SkylineGround", GroundShader);
            EnsureMaterial("SkylineSky", SkyShader);
            AssetDatabase.SaveAssets();
        }

        public static string MaterialPath(string name) => MaterialsFolder + "/" + name + ".mat";

        static void EnsureMaterial(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.Exception("Shader non trovato: " + shaderName);
            if (ShaderUtil.ShaderHasError(shader))
            {
                var sb = new StringBuilder("Lo shader ha errori: " + shaderName);
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    sb.Append("\n  ").Append(m.severity).Append(" riga ").Append(m.line).Append(": ").Append(m.message);
                throw new System.Exception(sb.ToString());
            }
            string path = MaterialPath(name);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            EditorUtility.SetDirty(mat);
        }
    }
}
