using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Underworld.Core;

namespace Underworld.UI
{
    /// <summary>
    /// Avvisi brevi (HudBus.Toast): pila di al massimo 3, il più nuovo in alto, spariscono da soli.
    /// La logica (durate, limite) è in <see cref="ToastQueue"/>; qui c'è solo la grafica.
    /// </summary>
    public sealed class ToastStack
    {
        public VisualElement Root { get; }

        readonly ToastQueue _queue = new ToastQueue();
        readonly Dictionary<int, VisualElement> _views = new Dictionary<int, VisualElement>();
        readonly List<ToastQueue.Entry> _expired = new List<ToastQueue.Entry>();

        public ToastStack()
        {
            Root = UiBuild.Box("uw-toasts", PickingMode.Ignore);
            Root.name = "toasts";
        }

        public void Push(string text, ToastKind kind)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var e = _queue.Push(text, kind, out var dropped);
            if (dropped != null) Remove(dropped.Id, true);

            var card = UiBuild.Box("uw-toast", PickingMode.Ignore);
            card.AddToClassList("uw-toast--" + kind.ToString().ToLowerInvariant());
            var icon = new UiIcon(IconFor(kind), 18f);
            icon.AddToClassList("uw-toast__icon");
            card.Add(icon);
            card.Add(UiBuild.Text(text, "uw-toast__text"));
            Root.Insert(0, card);
            _views[e.Id] = card;
            // Entra da destra di pochi pixel (ease-out, 200 ms).
            UiAnim.In(card, 0.2f, 0f, new Vector2(14f, 0f));
        }

        static IconKind IconFor(ToastKind kind) => kind switch
        {
            ToastKind.Success => IconKind.Check,
            ToastKind.Warning => IconKind.Warning,
            ToastKind.Error => IconKind.Error,
            _ => IconKind.Info,
        };

        public void Tick(float dt)
        {
            if (_queue.Items.Count == 0) return;
            _expired.Clear();
            _queue.Tick(dt, _expired);
            foreach (var e in _expired) Remove(e.Id, false);
        }

        void Remove(int id, bool fast)
        {
            if (!_views.TryGetValue(id, out var card)) return;
            _views.Remove(id);
            float t = fast ? 0.12f : UiAnim.ExitTime(0.2f);
            UiAnim.Out(card, t, new Vector2(10f, 0f), 1f, false, () =>
            {
                // Chiude lo spazio in modo morbido, poi toglie l'elemento.
                float h = card.resolvedStyle.height;
                float mb = card.resolvedStyle.marginBottom;
                float pt = card.resolvedStyle.paddingTop;
                float pb = card.resolvedStyle.paddingBottom;
                card.style.visibility = Visibility.Hidden;
                if (float.IsNaN(h) || h <= 0f) { card.RemoveFromHierarchy(); return; }
                UiAnim.Value(card, 0.14f, Ease.OutCubic, k =>
                {
                    card.style.height = Mathf.Lerp(h, 0f, k);
                    card.style.marginBottom = Mathf.Lerp(mb, 0f, k);
                    card.style.paddingTop = Mathf.Lerp(pt, 0f, k);
                    card.style.paddingBottom = Mathf.Lerp(pb, 0f, k);
                }, card.RemoveFromHierarchy, UiAnim.ChannelSize);
            });
        }

        public void Clear()
        {
            _queue.Clear();
            _views.Clear();
            Root.Clear();
        }
    }
}
