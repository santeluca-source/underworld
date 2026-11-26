using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Underworld.UI
{
    /// <summary>Nomi delle classi USS usate dal codice (definite in Assets/UI/Styles).</summary>
    public static class UiClasses
    {
        public const string Root = "uw-root";
        public const string Layer = "uw-layer";
        public const string Glass = "uw-glass";
        public const string GlassStrong = "uw-glass--strong";
        public const string Shadow = "uw-shadow";
        public const string Button = "uw-btn";
        public const string ButtonPrimary = "uw-btn--primary";
        public const string ButtonGhost = "uw-btn--ghost";
        public const string ButtonQuiet = "uw-btn--quiet";
        public const string ButtonIcon = "uw-btn--icon";
        public const string Field = "uw-field";
        public const string FieldLabel = "uw-field__label";
        public const string FieldHint = "uw-field__hint";
        public const string FieldInvalid = "uw-field--invalid";
        public const string FieldValid = "uw-field--valid";
        public const string Title = "uw-title";
        public const string Heading = "uw-heading";
        public const string Body = "uw-body";
        public const string Muted = "uw-muted";
        public const string Small = "uw-small";
        public const string Tabular = "uw-tabular";
        public const string Shadowed = "uw-text-shadow";
        public const string NoStagger = "uw-no-stagger";
        public const string Hidden = "uw-hidden";
    }

    /// <summary>
    /// Piccole fabbriche per gli elementi comuni (vetro, pulsanti, campi, testi).
    /// Gli stili veri sono nei fogli USS: qui si danno solo classi e struttura.
    /// </summary>
    public static class UiBuild
    {
        public static VisualElement Box(string className = null, PickingMode picking = PickingMode.Position)
        {
            var v = new VisualElement { pickingMode = picking };
            if (!string.IsNullOrEmpty(className)) v.AddToClassList(className);
            return v;
        }

        /// <summary>Contenitore a tutto schermo che non intercetta il mouse.</summary>
        public static VisualElement Layer(string name)
        {
            var v = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            v.AddToClassList(UiClasses.Layer);
            return v;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            return l;
        }

        /// <summary>Pannello di vetro con ombra morbida (massimo 1-2 per schermata).</summary>
        public static VisualElement Glass(string extraClass = null, bool shadow = true)
        {
            var v = new VisualElement();
            v.AddToClassList(UiClasses.Glass);
            if (!string.IsNullOrEmpty(extraClass)) v.AddToClassList(extraClass);
            if (shadow) UiShadow.Attach(v);
            return v;
        }

        /// <summary>
        /// Pulsante della UI. Toglie le classi del tema standard di Unity, così vale solo il nostro stile
        /// (pressione a scala 0,97 in USS).
        /// </summary>
        public static Button Button(string text, Action onClick, string variant = UiClasses.ButtonGhost)
        {
            var b = new Button(onClick) { text = text };
            b.RemoveFromClassList(UnityEngine.UIElements.Button.ussClassName);
            b.AddToClassList(UiClasses.Button);
            if (!string.IsNullOrEmpty(variant)) b.AddToClassList(variant);
            return b;
        }

        /// <summary>Pulsante quadrato con un'icona (es. chiudi).</summary>
        public static Button IconButton(IconKind icon, Action onClick, string tooltip = null)
        {
            var b = new Button(onClick) { text = string.Empty, tooltip = tooltip };
            b.RemoveFromClassList(UnityEngine.UIElements.Button.ussClassName);
            b.AddToClassList(UiClasses.Button);
            b.AddToClassList(UiClasses.ButtonIcon);
            b.Add(new UiIcon(icon, 16f) { Stroke = 2f });
            return b;
        }

        /// <summary>Campo di testo con etichetta sopra e riga di aiuto sotto.</summary>
        public static TextField Field(string label, string value, string placeholder, int maxLength, out VisualElement container, out Label hint)
        {
            container = new VisualElement();
            container.AddToClassList(UiClasses.Field);
            var lab = Text(label, UiClasses.FieldLabel);
            var tf = new TextField { value = value ?? string.Empty, maxLength = maxLength, isDelayed = false };
            tf.textEdition.placeholder = placeholder;
            tf.textEdition.hidePlaceholderOnFocus = false;
            tf.selectAllOnFocus = false;
            tf.AddToClassList("uw-textfield");
            hint = Text(string.Empty, UiClasses.FieldHint);
            container.Add(lab);
            container.Add(tf);
            container.Add(hint);
            return tf;
        }

        public static VisualElement Spacer(float height)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.height = height;
            v.style.flexShrink = 0;
            return v;
        }
    }

    /// <summary>
    /// Ombra morbida per i pannelli di vetro. UI Toolkit non ha box-shadow: uso una texture fatta a codice
    /// (rettangolo arrotondato sfocato, vuoto al centro come la box-shadow del CSS) divisa in 9 parti.
    /// </summary>
    public static class UiShadow
    {
        // Misure in punti di riferimento (1920x1080). Il raggio deve essere uguale a --uw-radius-panel.
        public const int Radius = 14;
        const int Blur = 30;          // estensione dell'ombra intorno al pannello
        const int OffsetY = 10;       // l'ombra scende un po' (luce dall'alto)
        const float Strength = 0.42f; // opacità massima dell'ombra

        static Texture2D _texture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _texture = null;

        static int Left => Blur;
        static int Right => Blur;
        static int Top => Blur - OffsetY;
        static int Bottom => Blur + OffsetY;
        const int Hole = Radius * 2 + 12;

        public static Texture2D Texture
        {
            get
            {
                if (_texture == null) _texture = Build();
                return _texture;
            }
        }

        /// <summary>Aggiunge l'ombra come primo figlio del pannello (posizione assoluta, non cliccabile).</summary>
        public static VisualElement Attach(VisualElement panel)
        {
            var s = new VisualElement { name = "shadow", pickingMode = PickingMode.Ignore };
            s.AddToClassList(UiClasses.Shadow);
            s.AddToClassList(UiClasses.NoStagger);
            s.style.position = Position.Absolute;
            // -1: il pannello ha un bordo di 1 px e i figli assoluti partono dentro il bordo.
            s.style.left = -Left - 1;
            s.style.right = -Right - 1;
            s.style.top = -Top - 1;
            s.style.bottom = -Bottom - 1;
            s.style.backgroundImage = new StyleBackground(Texture);
            s.style.unitySliceLeft = Left + Radius + 2;
            s.style.unitySliceRight = Right + Radius + 2;
            s.style.unitySliceTop = Top + Radius + 2;
            s.style.unitySliceBottom = Bottom + Radius + 2;
            panel.Insert(0, s);
            return s;
        }

        static Texture2D Build()
        {
            int w = Left + Hole + Right;
            int h = Top + Hole + Bottom;
            // Forma del pannello (il "buco") e forma dell'ombra (spostata in basso).
            Rect hole = new Rect(Left, Bottom, Hole, Hole); // y verso l'alto nella texture
            Rect blob = new Rect(Left, Bottom - OffsetY, Hole, Hole);
            float sigma = Blur / 2.6f;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = "UW_PanelShadow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                // Distanza con segno dal rettangolo arrotondato → ombra con decadimento gaussiano.
                float dBlob = RoundedRectDistance(p, blob, Radius);
                float shadow = dBlob <= 0f ? 1f : Mathf.Exp(-(dBlob * dBlob) / (2f * sigma * sigma));
                // Dentro il pannello niente ombra (bordo antialias di 1 px).
                float dHole = RoundedRectDistance(p, hole, Radius);
                float outside = Mathf.Clamp01(dHole + 0.5f);
                float a = shadow * outside * Strength;
                px[y * w + x] = new Color32(4, 6, 10, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static float RoundedRectDistance(Vector2 p, Rect r, float radius)
        {
            Vector2 c = r.center;
            Vector2 half = r.size * 0.5f - new Vector2(radius, radius);
            Vector2 d = new Vector2(Mathf.Abs(p.x - c.x) - half.x, Mathf.Abs(p.y - c.y) - half.y);
            Vector2 outside = new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f));
            return outside.magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - radius;
        }
    }
}
