using System.Collections.Generic;
using Underworld.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Underworld.Inventory
{
    /// <summary>
    /// Finestra "Inventory" (tasto I). Mostra nome, quantità e descrizione di ogni oggetto
    /// e un pulsante Use per gli oggetti usabili. I dati arrivano da PlayerInventory.Local (solo lettura):
    /// Use manda solo la richiesta al server.
    /// Righe e testi in Assets/Inventory/Resources/Underworld/Inventory.uss (stile vetro del gioco).
    /// </summary>
    public sealed class InventoryWindow : UiWindow
    {
        public const string Id = "inventory";
        const string StyleSheetPath = "Underworld/Inventory";

        public InventoryWindow() : base("Inventory", 560f)
        {
            AddToClassList("uw-inv");
            Rebuild();
        }

        protected internal override void OnOpened()
        {
            PlayerInventory.LocalChanged += Rebuild;
            Rebuild();
        }

        protected internal override void OnClosed()
        {
            PlayerInventory.LocalChanged -= Rebuild;
        }

        /// <summary>Righe da mostrare (pubblico per l'anteprima della UI).</summary>
        public static List<InventorySlot> CurrentSlots() =>
            PlayerInventory.Local != null ? PlayerInventory.Local.Snapshot() : PreviewSlots ?? new List<InventorySlot>();

        /// <summary>Righe finte per -uipreview (null nel gioco vero).</summary>
        internal static List<InventorySlot> PreviewSlots;

        void Rebuild()
        {
            Content.Clear();
            var slots = CurrentSlots();
            if (slots.Count == 0)
            {
                var empty = UiBuild.Box("uw-inv__empty", PickingMode.Ignore);
                empty.Add(UiBuild.Text("Your inventory is empty", UiClasses.Heading));
                empty.Add(UiBuild.Text("Items you buy or find will show up here.", UiClasses.Body, UiClasses.Muted));
                Content.Add(empty);
                return;
            }
            foreach (var slot in slots) Content.Add(BuildRow(slot));
        }

        VisualElement BuildRow(InventorySlot slot)
        {
            string id = slot.ItemId.ToString();
            ItemCatalog.TryGet(id, out var item);

            var row = UiBuild.Box("uw-inv__row");
            var text = UiBuild.Box("uw-inv__text", PickingMode.Ignore);
            var top = UiBuild.Box("uw-inv__top", PickingMode.Ignore);
            top.Add(UiBuild.Text(item != null ? item.DisplayName : id, "uw-inv__name"));
            top.Add(UiBuild.Text("x" + slot.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                "uw-inv__count", UiClasses.Tabular));
            text.Add(top);
            if (item != null && !string.IsNullOrEmpty(item.Description))
                text.Add(UiBuild.Text(item.Description, UiClasses.Body, UiClasses.Muted, "uw-inv__desc"));
            row.Add(text);

            if (item != null && item.Usable)
            {
                var use = UiBuild.Button("Use", () => PlayerInventory.Local?.RequestUse(id));
                use.AddToClassList("uw-inv__use");
                row.Add(use);
            }
            return row;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterUi()
        {
            UiRegistry.RegisterWindow(Id, () => new InventoryWindow());
            var sheet = Resources.Load<StyleSheet>(StyleSheetPath);
            if (sheet != null) UiRegistry.RegisterStyleSheet(sheet);
            else Debug.LogWarning($"[UI] Foglio di stile {StyleSheetPath} mancante: l'inventario usa lo stile base.");
        }
    }
}
