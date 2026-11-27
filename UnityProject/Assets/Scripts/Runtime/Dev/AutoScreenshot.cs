using System.Collections;
using System.Globalization;
using Underworld.Core;
using UnityEngine;

namespace Underworld.Dev
{
    /// <summary>
    /// Screenshot automatico del gioco vero: avvia con  -autoshot percorso.png [-autoshot-delay s]
    /// Dopo il ritardo (default 3 s) salva lo screenshot e chiude. Nasce da solo al primo caricamento.
    /// Non fa niente: senza -autoshot, sul server, con -autotest (gli scenari fanno i loro screenshot)
    /// e con -uipreview (l'anteprima della UI gestisce -autoshot da sola).
    /// </summary>
    public sealed class AutoScreenshot : MonoBehaviour
    {
        /// <summary>Vero se questo processo deve fare lo screenshot automatico.</summary>
        public static bool ShouldRun(LaunchArgs args) =>
            !string.IsNullOrEmpty(args.AutoShot) && !args.IsServer && !Application.isBatchMode &&
            string.IsNullOrEmpty(args.AutoTest) && !args.Has("uipreview") && !args.Has("preview");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!ShouldRun(LaunchArgs.Current)) return;
            var go = new GameObject("AutoScreenshot");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoScreenshot>();
        }

        IEnumerator Start()
        {
            var args = LaunchArgs.Current;
            string path = args.AutoShot;
            float delay = 3f;
            if (float.TryParse(args.Get("autoshot-delay", "3"), NumberStyles.Float, CultureInfo.InvariantCulture, out float d)) delay = Mathf.Max(0.5f, d);

            yield return new WaitForSecondsRealtime(delay);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[AutoScreenshot] salvato " + path);
            if (GameServices.Launcher != null) GameServices.Launcher.QuitGame();
            else Application.Quit();
        }
    }
}
