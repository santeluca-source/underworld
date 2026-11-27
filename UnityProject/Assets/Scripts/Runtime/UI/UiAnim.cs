using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Underworld.UI
{
    /// <summary>Curve di animazione. Regola del vault: ease-out per le entrate, niente rimbalzi.</summary>
    public enum Ease { Linear, OutCubic, OutQuart, OutQuint, InOutCubic }

    public static class Easing
    {
        public static float Apply(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            switch (ease)
            {
                case Ease.OutCubic: return 1f - u * u * u;
                case Ease.OutQuart: return 1f - u * u * u * u;
                case Ease.OutQuint: return 1f - u * u * u * u * u;
                case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                default: return t;
            }
        }
    }

    /// <summary>
    /// Motore delle animazioni della UI. Gira nel tempo reale (non scalato) dentro UiRoot.Update.
    /// Ogni animazione ha un elemento e un "canale": una nuova animazione sullo stesso canale sostituisce la vecchia.
    /// </summary>
    public sealed class UiTweener
    {
        struct Tween
        {
            public VisualElement Element;
            public int Channel;
            public float Delay;
            public float Duration;
            public float Time;
            public Ease Ease;
            public Action<float> Apply;
            public Action Done;
        }

        readonly List<Tween> _tweens = new List<Tween>(64);
        readonly List<Action> _finished = new List<Action>(16);

        public int Count => _tweens.Count;

        public void Start(VisualElement element, int channel, float duration, Ease ease, Action<float> apply, Action done = null, float delay = 0f)
        {
            Cancel(element, channel);
            _tweens.Add(new Tween
            {
                Element = element, Channel = channel, Delay = Mathf.Max(0f, delay),
                Duration = Mathf.Max(0.0001f, duration), Ease = ease, Apply = apply, Done = done,
            });
            // Primo valore subito: evita un frame con lo stato vecchio.
            if (delay <= 0f) apply?.Invoke(Easing.Apply(ease, 0f));
        }

        /// <summary>Ferma l'animazione del canale senza chiamare Done.</summary>
        public void Cancel(VisualElement element, int channel)
        {
            for (int i = _tweens.Count - 1; i >= 0; i--)
                if (_tweens[i].Element == element && _tweens[i].Channel == channel) _tweens.RemoveAt(i);
        }

        public bool IsRunning(VisualElement element, int channel)
        {
            for (int i = 0; i < _tweens.Count; i++)
                if (_tweens[i].Element == element && _tweens[i].Channel == channel) return true;
            return false;
        }

        public void Update(float dt)
        {
            for (int i = 0; i < _tweens.Count; i++)
            {
                var tw = _tweens[i];
                tw.Time += dt;
                float local = tw.Time - tw.Delay;
                if (local < 0f) { _tweens[i] = tw; continue; }
                float t = Mathf.Clamp01(local / tw.Duration);
                tw.Apply?.Invoke(Easing.Apply(tw.Ease, t));
                if (t >= 1f)
                {
                    if (tw.Done != null) _finished.Add(tw.Done);
                    _tweens.RemoveAt(i);
                    i--;
                    continue;
                }
                _tweens[i] = tw;
            }
            // Done dopo il ciclo: un Done può avviare nuove animazioni.
            for (int i = 0; i < _finished.Count; i++) _finished[i]();
            _finished.Clear();
        }

        public void Clear() => _tweens.Clear();
    }

    /// <summary>
    /// Animazioni pronte, con i tempi delle regole del vault:
    /// entrate ease-out 150-250 ms, uscite più corte del 20 %, liste sfalsate di 30 ms, popover da 0,95.
    /// Senza UiRoot (test, server) applicano subito lo stato finale.
    /// </summary>
    public static class UiAnim
    {
        public const float EnterTime = 0.22f;
        public const float FastEnterTime = 0.16f;
        public const float StaggerStep = 0.03f;
        public const float PopoverScale = 0.95f;

        public const int ChannelVisibility = 1;
        public const int ChannelPulse = 2;
        public const int ChannelValue = 3;
        public const int ChannelSize = 4;
        public const int ChannelExtra = 5;

        /// <summary>Durata di un'uscita: 20 % più corta dell'entrata.</summary>
        public static float ExitTime(float enter) => enter * 0.8f;

        static UiTweener Tweener => UiRoot.Instance != null ? UiRoot.Instance.Tweener : null;

        /// <summary>
        /// Mostra un elemento: dissolvenza da 0, spostamento da <paramref name="from"/> e scala da <paramref name="fromScale"/>.
        /// Se l'elemento stava uscendo, riparte dal punto in cui era (niente salti).
        /// </summary>
        public static void In(VisualElement el, float duration = EnterTime, float delay = 0f, Vector2 from = default,
            float fromScale = 1f, Action done = null, Ease ease = Ease.OutQuart)
        {
            if (el == null) return;
            el.style.display = DisplayStyle.Flex;
            var tw = Tweener;
            if (tw == null) { ClearInline(el); done?.Invoke(); return; }

            bool wasRunning = tw.IsRunning(el, ChannelVisibility);
            float startOpacity = wasRunning ? el.style.opacity.value : 0f;
            Vector2 startPos = from;
            float startScale = fromScale;
            if (wasRunning)
            {
                var tr = el.style.translate.value;
                startPos = new Vector2(tr.x.value, tr.y.value);
                startScale = el.style.scale.value.value.x;
            }
            if (delay > 0f) Apply(el, startOpacity, startPos, startScale);
            tw.Start(el, ChannelVisibility, duration, ease, k =>
            {
                Apply(el, Mathf.Lerp(startOpacity, 1f, k), Vector2.Lerp(startPos, Vector2.zero, k), Mathf.Lerp(startScale, 1f, k));
            }, () =>
            {
                // Alla fine tolgo gli stili in linea: così valgono di nuovo quelli del USS (es. :active a 0,97).
                ClearInline(el);
                done?.Invoke();
            }, delay);
        }

        /// <summary>Toglie opacità, spostamento e scala scritti dal codice (torna lo stile del USS).</summary>
        public static void ClearInline(VisualElement el)
        {
            el.style.opacity = StyleKeyword.Null;
            el.style.translate = StyleKeyword.Null;
            el.style.scale = StyleKeyword.Null;
        }

        /// <summary>
        /// Nasconde un elemento: dissolvenza verso 0, spostamento verso <paramref name="to"/>.
        /// Alla fine mette display:none se <paramref name="hide"/> è true.
        /// </summary>
        public static void Out(VisualElement el, float duration = 0f, Vector2 to = default, float toScale = 1f,
            bool hide = true, Action done = null, float delay = 0f)
        {
            if (el == null) return;
            if (duration <= 0f) duration = ExitTime(EnterTime);
            var tw = Tweener;
            if (tw == null || el.style.display == DisplayStyle.None)
            {
                tw?.Cancel(el, ChannelVisibility);
                Apply(el, 0f, to, toScale);
                if (hide) el.style.display = DisplayStyle.None;
                done?.Invoke();
                return;
            }
            float startOpacity = el.style.opacity.keyword == StyleKeyword.Undefined ? el.style.opacity.value : 1f;
            var tr = el.style.translate.value;
            Vector2 startPos = new Vector2(tr.x.value, tr.y.value);
            float startScale = el.style.scale.keyword == StyleKeyword.Undefined ? el.style.scale.value.value.x : 1f;
            tw.Start(el, ChannelVisibility, duration, Ease.OutCubic, k =>
            {
                Apply(el, Mathf.Lerp(startOpacity, 0f, k), Vector2.Lerp(startPos, to, k), Mathf.Lerp(startScale, toScale, k));
            }, () =>
            {
                if (hide) el.style.display = DisplayStyle.None;
                done?.Invoke();
            }, delay);
        }

        /// <summary>Mostra o nasconde subito, senza animazione.</summary>
        public static void Set(VisualElement el, bool visible)
        {
            if (el == null) return;
            Tweener?.Cancel(el, ChannelVisibility);
            if (visible) ClearInline(el); else Apply(el, 0f, Vector2.zero, 1f);
            el.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Fa entrare i figli uno dopo l'altro (30 ms di distanza), dal basso di qualche pixel.</summary>
        public static void StaggerChildren(VisualElement parent, float baseDelay = 0f, float distance = 8f, float duration = EnterTime)
        {
            if (parent == null) return;
            int i = 0;
            foreach (var child in parent.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None && child.style.display == DisplayStyle.None) continue;
                if (child.ClassListContains(UiClasses.NoStagger)) continue;
                In(child, duration, baseDelay + i * StaggerStep, new Vector2(0f, distance));
                i++;
            }
        }

        /// <summary>"Pulse" di conferma: scala fino a <paramref name="peak"/> e torna a 1. Niente rimbalzo.</summary>
        public static void Pulse(VisualElement el, float peak = 1.06f, float duration = 0.26f)
        {
            if (el == null) return;
            var tw = Tweener;
            if (tw == null) return;
            tw.Start(el, ChannelPulse, duration, Ease.Linear, k =>
            {
                // Sale veloce (primo 35 %) e scende morbido.
                float s = k < 0.35f ? Easing.Apply(Ease.OutCubic, k / 0.35f) : 1f - Easing.Apply(Ease.InOutCubic, (k - 0.35f) / 0.65f);
                float v = 1f + (peak - 1f) * s;
                el.style.scale = new Scale(new Vector3(v, v, 1f));
            }, () => el.style.scale = StyleKeyword.Null);
        }

        /// <summary>Anima un valore qualsiasi (es. il contatore dei soldi).</summary>
        public static void Value(VisualElement owner, float duration, Ease ease, Action<float> apply, Action done = null, int channel = ChannelValue, float delay = 0f)
        {
            var tw = Tweener;
            if (tw == null) { apply?.Invoke(1f); done?.Invoke(); return; }
            tw.Start(owner, channel, duration, ease, apply, done, delay);
        }

        public static void Cancel(VisualElement el, int channel) => Tweener?.Cancel(el, channel);

        static void Apply(VisualElement el, float opacity, Vector2 pos, float scale)
        {
            el.style.opacity = opacity;
            el.style.translate = new Translate(pos.x, pos.y);
            el.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }
    }
}
