using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Underworld.Core;
using Underworld.Player;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Underworld.World.Dev
{
    /// <summary>
    /// Modo anteprima del mondo, senza rete (solo per lo sviluppo).
    /// Avvio: UNDERWORLD.exe -preview -preview-out C:\cartella [-area Downtown] [-preview-shots plaza_street,bank] [-preview-navmesh]
    ///        -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile log.txt
    /// Carica la scena dell'area se serve, aspetta il mondo, muove la camera sulle inquadrature del costruttore
    /// (BuildContext.PreviewShot), salva uno screenshot PNG per ognuna, scrive nel log batch, triangoli e FPS, poi chiude.
    /// Senza scheda video (-batchmode -nographics) scrive solo i dati della costruzione e chiude.
    /// </summary>
    public sealed class WorldPreview : MonoBehaviour
    {
        const int WarmupFrames = 30;
        const int MeasureFrames = 90;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!LaunchArgs.Current.Has("preview")) return;
            var go = new GameObject("WorldPreview");
            DontDestroyOnLoad(go);
            go.AddComponent<WorldPreview>();
        }

        IEnumerator Start()
        {
            var args = LaunchArgs.Current;
            string outDir = args.Get("preview-out", Path.Combine(Application.persistentDataPath, "Preview"));
            Directory.CreateDirectory(outDir);

            // La scena dell'area (se il gioco è partito da un'altra scena, la carichiamo noi).
            if (AreaBuilder.Current == null)
            {
                string scene = AreaInfo.SceneName(args.Area);
                Debug.Log($"[Preview] Carico la scena {scene}");
                SceneManager.LoadScene(scene, LoadSceneMode.Single);
            }
            float limit = Time.realtimeSinceStartup + 30f;
            while ((AreaBuilder.Current == null || !AreaBuilder.Current.IsBuilt) && Time.realtimeSinceStartup < limit) yield return null;
            var builder = AreaBuilder.Current;
            if (builder == null || !builder.IsBuilt)
            {
                Debug.LogError("[Preview] Il mondo non è stato costruito entro 30 s.");
                Quit(2);
                yield break;
            }
            var result = builder.Result;
            if (args.Has("preview-navmesh")) builder.EnsureNavMesh();
            Debug.Log($"[Preview] Mondo {result.Area}: build={result.BuildMs:0} ms navmesh={result.NavMeshMs:0} ms {result}");

            if (!result.Graphics || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[Preview] Senza grafica: niente screenshot.");
                Quit(0);
                yield break;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[Preview] Nessuna Main Camera nella scena.");
                Quit(3);
                yield break;
            }
            var tpc = cam.GetComponent<ThirdPersonCamera>();
            if (tpc != null) tpc.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var only = ParseList(args.Get("preview-shots"));
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");

            var summary = new StringBuilder("[Preview] Riepilogo:\n");
            foreach (var shot in result.Shots)
            {
                if (only != null && !only.Contains(shot.Name)) continue;
                cam.transform.SetPositionAndRotation(shot.Position, Quaternion.LookRotation(shot.LookAt - shot.Position));
                for (int i = 0; i < WarmupFrames; i++) yield return null;

                float t0 = Time.realtimeSinceStartup;
                long maxBatches = 0, maxSetPass = 0, maxDraws = 0, maxTris = 0;
                for (int i = 0; i < MeasureFrames; i++)
                {
                    yield return null;
                    maxBatches = Max(maxBatches, batches);
                    maxSetPass = Max(maxSetPass, setPass);
                    maxDraws = Max(maxDraws, draws);
                    maxTris = Max(maxTris, tris);
                }
                float fps = MeasureFrames / Mathf.Max(0.0001f, Time.realtimeSinceStartup - t0);

                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                string file = Path.Combine(outDir, shot.Name + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Destroy(tex);

                string stats = string.Format(CultureInfo.InvariantCulture,
                    "{0}: fps={1:0} batches={2} setpass={3} drawcalls={4} tris={5} -> {6}",
                    shot.Name, fps, Show(batches, maxBatches), Show(setPass, maxSetPass), Show(draws, maxDraws), Show(tris, maxTris), file);
                Debug.Log("[Preview] " + stats);
                summary.AppendLine("  " + stats);
            }
            Debug.Log(summary.ToString());
            Quit(0);
        }

        static long Max(long current, ProfilerRecorder r) => r.Valid && r.LastValue > current ? r.LastValue : current;

        // Alcuni contatori esistono solo nei player di sviluppo: allora scriviamo "n/d".
        static string Show(ProfilerRecorder r, long value) => r.Valid ? value.ToString(CultureInfo.InvariantCulture) : "n/d";

        static HashSet<string> ParseList(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var set = new HashSet<string>();
            foreach (var part in s.Split(',')) if (part.Trim().Length > 0) set.Add(part.Trim());
            return set;
        }

        static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }
    }
}
