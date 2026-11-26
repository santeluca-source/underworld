using UnityEngine;

namespace Underworld.Dev
{
    /// <summary>Comandi a schermo e FPS. Solo per lo sviluppo.</summary>
    public class DevHud : MonoBehaviour
    {
        float _fps = 60f;
        GUIStyle _style;

        void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);
        }

        void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 16 };
                _style.normal.textColor = Color.white;
            }
            GUI.Label(new Rect(14, 10, 900, 26), "UNDERWORLD - Downtown (prova)", _style);
            GUI.Label(new Rect(14, 34, 1000, 26), "WASD muovi | Shift corri | Spazio salta | Mouse guarda | Rotella zoom", _style);
            GUI.Label(new Rect(14, 58, 300, 26), "FPS " + Mathf.RoundToInt(_fps), _style);
        }
    }
}
