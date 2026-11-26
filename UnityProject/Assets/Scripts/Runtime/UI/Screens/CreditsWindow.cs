using UnityEngine;
using UnityEngine.UIElements;

namespace Underworld.UI
{
    /// <summary>Finestra dei crediti (dal menu principale). Registrata da UiRoot con l'id "credits".</summary>
    public sealed class CreditsWindow : UiWindow
    {
        public const string Id = "credits";

        public CreditsWindow() : base("Crediti", 520f)
        {
            AddRow("Gioco", "Team UNDERWORLD");
            AddRow("Sviluppo", "Claude (Anthropic) con il team");
            AddRow("Modelli 3D", "Kenney · kenney.nl · licenza CC0");
            AddRow("Carattere", "Inter di Rasmus Andersson · licenza SIL OFL 1.1");
            AddRow("Motore", "Unity 6 · Netcode for GameObjects");
            Content.Add(UiBuild.Text("Grazie a chi crea risorse libere per tutti", UiClasses.Small, UiClasses.Muted, "uw-credits__thanks"));
        }

        void AddRow(string role, string who)
        {
            var row = UiBuild.Box("uw-credits__row", PickingMode.Ignore);
            row.Add(UiBuild.Text(role, "uw-credits__role"));
            row.Add(UiBuild.Text(who, "uw-credits__who"));
            Content.Add(row);
        }
    }
}
