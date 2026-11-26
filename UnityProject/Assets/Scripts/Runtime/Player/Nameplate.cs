using UnityEngine;

namespace Underworld.Player
{
    /// <summary>
    /// Targhetta col nome sopra la testa di un giocatore. Guarda sempre verso la camera (billboard).
    /// Fatta con TextMesh (nessuna dipendenza dalla UI). Si rimpicciolisce da lontano e sparisce oltre 60 m.
    /// </summary>
    public sealed class Nameplate : MonoBehaviour
    {
        public float maxDistance = 60f;

        TextMesh _text;
        MeshRenderer _renderer;
        Camera _cam;

        /// <summary>Crea la targhetta come figlia del personaggio.</summary>
        public static Nameplate Create(Transform owner, float height)
        {
            var go = new GameObject("Nameplate");
            go.transform.SetParent(owner, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);

            var text = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            text.fontSize = 48;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;

            var mr = go.GetComponent<MeshRenderer>();
            if (font != null) mr.sharedMaterial = font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var plate = go.AddComponent<Nameplate>();
            plate._text = text;
            plate._renderer = mr;
            return plate;
        }

        public void SetText(string value)
        {
            if (_text != null) _text.text = value ?? string.Empty;
        }

        void LateUpdate()
        {
            if (_cam == null || !_cam.isActiveAndEnabled)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }
            Vector3 toCam = transform.position - _cam.transform.position;
            float dist = toCam.magnitude;
            bool visible = dist < maxDistance;
            if (_renderer.enabled != visible) _renderer.enabled = visible;
            if (!visible || dist < 0.01f) return;
            transform.rotation = Quaternion.LookRotation(toCam / dist, Vector3.up);
            // Leggibile anche da lontano: cresce un po' con la distanza.
            float s = Mathf.Clamp(dist / 10f, 0.6f, 2.2f);
            transform.localScale = new Vector3(s, s, s);
        }
    }
}
