using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Underworld.EditorTools
{
    /// <summary>
    /// Un passo del setup del progetto (crea asset, prefab, scene...).
    /// Ogni modulo aggiunge le sue classi che implementano questa interfaccia:
    /// il runner le trova da solo (TypeCache). Nessun file centrale da modificare.
    /// Ordini consigliati: 0-99 impostazioni, 100-199 asset base e arte, 200-399 prefab,
    /// 400-599 scene, 900+ controlli finali.
    /// </summary>
    public interface IProjectSetupStep
    {
        int Order { get; }
        string Name { get; }
        void Run(SetupContext ctx);
    }

    /// <summary>Dati e aiuti condivisi tra i passi del setup.</summary>
    public sealed class SetupContext
    {
        public const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset"; // stessa lista che Netcode crea da solo

        readonly List<(string path, int priority)> _scenes = new List<(string, int)>();

        /// <summary>Scene della build. Priorità più bassa = indice più basso (Bootstrap = 0).</summary>
        public IReadOnlyList<(string path, int priority)> Scenes => _scenes;

        public void AddBuildScene(string path, int priority)
        {
            _scenes.RemoveAll(s => s.path == path);
            _scenes.Add((path, priority));
        }

        /// <summary>Lista unica dei prefab di rete (asset condiviso da tutti i moduli).</summary>
        public NetworkPrefabsList GetNetworkPrefabs()
        {
            EnsureFolder("Assets/Settings");
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, NetworkPrefabsPath);
            }
            return list;
        }

        /// <summary>Aggiunge un prefab (asset, con NetworkObject) alla lista dei prefab di rete.</summary>
        public void RegisterNetworkPrefab(GameObject prefabAsset)
        {
            if (prefabAsset == null) throw new ArgumentNullException(nameof(prefabAsset));
            var list = GetNetworkPrefabs();
            if (!list.Contains(prefabAsset))
            {
                list.Add(new NetworkPrefab { Prefab = prefabAsset });
                EditorUtility.SetDirty(list);
            }
        }

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }

    /// <summary>
    /// Esegue tutti i passi del setup e crea la build.
    /// Riga di comando:
    ///   -executeMethod Underworld.EditorTools.UnderworldSetup.Run
    ///   -executeMethod Underworld.EditorTools.UnderworldSetup.BuildWindows
    /// In batchmode entrambi chiudono Unity con codice 0 (ok) o 1 (errore).
    /// </summary>
    public static class UnderworldSetup
    {
        [MenuItem("UNDERWORLD/Setup completo")]
        public static void Run()
        {
            bool ok = RunAll();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool RunAll()
        {
            var ctx = new SetupContext();
            var steps = TypeCache.GetTypesDerivedFrom<IProjectSetupStep>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (IProjectSetupStep)Activator.CreateInstance(t))
                .OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.Ordinal)
                .ToList();

            foreach (var step in steps)
            {
                Debug.Log($"[UNDERWORLD] Passo {step.Order}: {step.Name}");
                try
                {
                    step.Run(ctx);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UNDERWORLD] Passo fallito: {step.Name}\n{e}");
                    return false;
                }
            }

            EditorBuildSettings.scenes = ctx.Scenes
                .OrderBy(s => s.priority)
                .Select(s => new EditorBuildSettingsScene(s.path, true))
                .ToArray();

            AssetDatabase.SaveAssets();
            Debug.Log($"[UNDERWORLD] Setup completato: {steps.Count} passi, {ctx.Scenes.Count} scene.");
            return true;
        }

        [MenuItem("UNDERWORLD/Build Windows")]
        public static void BuildWindows()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/Windows/UNDERWORLD.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            bool ok = report.summary.result == BuildResult.Succeeded;
            Debug.Log($"[UNDERWORLD] Build: {report.summary.result} errori={report.summary.totalErrors} scene={scenes.Length}");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
