using UnityEditor;
using UnityEngine;

namespace Underworld.EditorTools
{
    /// <summary>Impostazioni del progetto: nome, finestra, colori, input.</summary>
    public sealed class ProjectSettingsStep : IProjectSetupStep
    {
        public int Order => 0;
        public string Name => "Impostazioni progetto";

        public void Run(SetupContext ctx)
        {
            PlayerSettings.companyName = "Underworld";
            PlayerSettings.productName = "UNDERWORLD";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true; // server e client di prova girano anche senza focus
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Input: "Both" (Input System nuovo + vecchio) per compatibilità con i pacchetti.
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets != null && assets.Length > 0)
            {
                var so = new SerializedObject(assets[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 2)
                {
                    prop.intValue = 2;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
