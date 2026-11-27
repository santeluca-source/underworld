namespace Underworld.Core
{
    /// <summary>Le aree del mondo. Ogni area gira in un server separato.</summary>
    public enum AreaId
    {
        Downtown = 0,
        Industrial = 1,
        Suburbs = 2,
        Outskirts = 3,
        /// <summary>Piano vuoto e leggero per i test automatici.</summary>
        TestGround = 9,
    }

    /// <summary>Dati fissi delle aree: scena, porta standard, nome da mostrare (in inglese).</summary>
    public static class AreaInfo
    {
        public static string SceneName(AreaId area) => "Area_" + area;

        public static ushort DefaultPort(AreaId area) => area switch
        {
            AreaId.Downtown => 7777,
            AreaId.Industrial => 7778,
            AreaId.Suburbs => 7779,
            AreaId.Outskirts => 7780,
            _ => 7790,
        };

        public static string DisplayName(AreaId area) => area switch
        {
            AreaId.Downtown => "Downtown",
            AreaId.Industrial => "Industrial District",
            AreaId.Suburbs => "Suburbs",
            AreaId.Outskirts => "Outskirts",
            _ => "Test Ground",
        };

        /// <summary>Le aree giocabili (senza TestGround).</summary>
        public static readonly AreaId[] Playable = { AreaId.Downtown, AreaId.Industrial, AreaId.Suburbs, AreaId.Outskirts };
    }
}
