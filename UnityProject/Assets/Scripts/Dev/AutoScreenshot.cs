using System.Collections;
using UnityEngine;

namespace Underworld.Dev
{
    /// <summary>
    /// Test automatico: avvia il gioco con  -autoshot percorso.png
    /// Dopo 3 secondi salva uno screenshot e chiude. Senza l'argomento non fa niente.
    /// </summary>
    public class AutoScreenshot : MonoBehaviour
    {
        IEnumerator Start()
        {
            string path = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-autoshot") path = args[i + 1];
            if (path == null) yield break;

            yield return new WaitForSeconds(3f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(1f);
            Debug.Log("[AutoScreenshot] salvato " + path);
            Application.Quit();
        }
    }
}
