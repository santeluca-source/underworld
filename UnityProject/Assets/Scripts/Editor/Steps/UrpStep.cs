using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Underworld.EditorTools
{
    /// <summary>Crea l'asset URP e lo assegna a tutti i livelli di qualità.</summary>
    public sealed class UrpStep : IProjectSetupStep
    {
        public const string AssetPath = "Assets/Settings/URP_Asset.asset";
        public const string RendererPath = "Assets/Settings/URP_Renderer.asset";

        public int Order => 10;
        public string Name => "Pipeline grafica URP";

        public void Run(SetupContext ctx)
        {
            SetupContext.EnsureFolder("Assets/Settings");
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (rp == null)
            {
                var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                if (rd == null)
                {
                    rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rd, RendererPath);
                }
                rp = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(rp, AssetPath);
            }
            rp.shadowDistance = 140f;
            rp.msaaSampleCount = 4;
            rp.supportsHDR = true;
            EditorUtility.SetDirty(rp);

            GraphicsSettings.defaultRenderPipeline = rp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = rp;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>Shader Lit di URP (con ripiego su Standard).</summary>
        public static Shader LitShader()
        {
            var s = Shader.Find("Universal Render Pipeline/Lit");
            return s != null ? s : Shader.Find("Standard");
        }
    }
}
