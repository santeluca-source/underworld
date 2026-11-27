using UnityEngine;
using UnityEngine.UIElements;

namespace Underworld.UI
{
    public enum IconKind
    {
        None, Star, Check, Info, Warning, Error, Close, Target, ChevronLeft, Arc, Dot,
    }

    /// <summary>
    /// Icona disegnata a vettori (Painter2D): niente immagini da importare, nitida a ogni risoluzione.
    /// Il colore si legge da "color" (USS) così le classi possono cambiarlo; <see cref="Filled"/> sceglie pieno o contorno.
    /// </summary>
    public class UiIcon : VisualElement
    {
        IconKind _kind;
        bool _filled = true;
        float _stroke = 2f;

        public IconKind Kind
        {
            get => _kind;
            set { if (_kind == value) return; _kind = value; MarkDirtyRepaint(); }
        }

        public bool Filled
        {
            get => _filled;
            set { if (_filled == value) return; _filled = value; MarkDirtyRepaint(); }
        }

        /// <summary>Spessore della linea in punti (riferimento 1920x1080).</summary>
        public float Stroke
        {
            get => _stroke;
            set { _stroke = value; MarkDirtyRepaint(); }
        }

        public UiIcon() : this(IconKind.None) { }

        public UiIcon(IconKind kind, float size = 16f)
        {
            _kind = kind;
            pickingMode = PickingMode.Ignore;
            AddToClassList("uw-icon");
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 1f || r.height < 1f || _kind == IconKind.None) return;
            Color c = resolvedStyle.color;
            var p = ctx.painter2D;
            p.lineWidth = _stroke;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = c;
            p.fillColor = c;
            float s = Mathf.Min(r.width, r.height);
            Vector2 o = new Vector2(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f);
            Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);

            switch (_kind)
            {
                case IconKind.Star:
                    p.BeginPath();
                    for (int i = 0; i < 10; i++)
                    {
                        float a = -Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                        float rad = (i % 2 == 0) ? 0.5f : 0.22f;
                        Vector2 v = P(0.5f + Mathf.Cos(a) * rad, 0.53f + Mathf.Sin(a) * rad);
                        if (i == 0) p.MoveTo(v); else p.LineTo(v);
                    }
                    p.ClosePath();
                    if (_filled) p.Fill(); else { p.lineWidth = Mathf.Max(1.2f, _stroke * 0.75f); p.Stroke(); }
                    break;

                case IconKind.Check:
                    p.BeginPath();
                    p.MoveTo(P(0.2f, 0.52f));
                    p.LineTo(P(0.42f, 0.72f));
                    p.LineTo(P(0.8f, 0.3f));
                    p.Stroke();
                    break;

                case IconKind.Close:
                    p.BeginPath();
                    p.MoveTo(P(0.25f, 0.25f)); p.LineTo(P(0.75f, 0.75f));
                    p.MoveTo(P(0.75f, 0.25f)); p.LineTo(P(0.25f, 0.75f));
                    p.Stroke();
                    break;

                case IconKind.ChevronLeft:
                    p.BeginPath();
                    p.MoveTo(P(0.62f, 0.2f)); p.LineTo(P(0.32f, 0.5f)); p.LineTo(P(0.62f, 0.8f));
                    p.Stroke();
                    break;

                case IconKind.Info:
                case IconKind.Warning:
                case IconKind.Error:
                    // Cerchio pieno con il segno "scavato" nel colore del testo scuro.
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), s * 0.5f, 0f, 360f);
                    p.Fill();
                    p.strokeColor = new Color(0.04f, 0.05f, 0.07f, 0.92f);
                    p.fillColor = p.strokeColor;
                    p.lineWidth = Mathf.Max(1.5f, s * 0.12f);
                    if (_kind == IconKind.Info)
                    {
                        p.BeginPath(); p.MoveTo(P(0.5f, 0.46f)); p.LineTo(P(0.5f, 0.72f)); p.Stroke();
                        p.BeginPath(); p.Arc(P(0.5f, 0.29f), s * 0.07f, 0f, 360f); p.Fill();
                    }
                    else if (_kind == IconKind.Warning)
                    {
                        p.BeginPath(); p.MoveTo(P(0.5f, 0.26f)); p.LineTo(P(0.5f, 0.55f)); p.Stroke();
                        p.BeginPath(); p.Arc(P(0.5f, 0.72f), s * 0.07f, 0f, 360f); p.Fill();
                    }
                    else
                    {
                        p.BeginPath();
                        p.MoveTo(P(0.34f, 0.34f)); p.LineTo(P(0.66f, 0.66f));
                        p.MoveTo(P(0.66f, 0.34f)); p.LineTo(P(0.34f, 0.66f));
                        p.Stroke();
                    }
                    break;

                case IconKind.Target:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), s * 0.5f - _stroke * 0.5f, 0f, 360f);
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), s * 0.17f, 0f, 360f);
                    p.Fill();
                    break;

                case IconKind.Arc:
                    // Arco di 100° per lo spinner (la rotazione la fa l'elemento).
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), s * 0.5f - _stroke * 0.5f, -90f, 20f);
                    p.Stroke();
                    break;

                case IconKind.Dot:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), s * 0.5f, 0f, 360f);
                    if (_filled) p.Fill(); else { p.lineWidth = Mathf.Max(1.2f, _stroke * 0.75f); p.Stroke(); }
                    break;
            }
        }
    }
}
