using System.Collections.Generic;
using UnityEngine;

namespace Underworld.Core
{
    /// <summary>Tipi di punti speciali di un'area.</summary>
    public enum AnchorKind
    {
        Spawn, Arrival, Exit, Shop, Dealership, Garage, Property, MissionBoard,
        PoliceStation, Bank, ClothingStore, Hideout, Fence, Robbery, Other,
    }

    /// <summary>Elenco dei punti speciali dell'area caricata.</summary>
    public static class AreaAnchors
    {
        static readonly Dictionary<string, AreaAnchor> ById = new Dictionary<string, AreaAnchor>();
        static readonly List<AreaAnchor> AllAnchors = new List<AreaAnchor>();

        public static IReadOnlyList<AreaAnchor> All => AllAnchors;

        internal static void Register(AreaAnchor a)
        {
            if (a == null || string.IsNullOrEmpty(a.Id)) return;
            if (ById.TryGetValue(a.Id, out var old) && old != a)
                Debug.LogWarning($"[AreaAnchors] Id duplicato: {a.Id}");
            ById[a.Id] = a;
            if (!AllAnchors.Contains(a)) AllAnchors.Add(a);
        }

        internal static void Unregister(AreaAnchor a)
        {
            if (a == null) return;
            if (!string.IsNullOrEmpty(a.Id) && ById.TryGetValue(a.Id, out var cur) && cur == a) ById.Remove(a.Id);
            AllAnchors.Remove(a);
        }

        public static AreaAnchor Find(string id) => id != null && ById.TryGetValue(id, out var a) ? a : null;

        public static List<AreaAnchor> OfKind(AnchorKind kind)
        {
            var list = new List<AreaAnchor>();
            foreach (var a in AllAnchors) if (a.Kind == kind) list.Add(a);
            return list;
        }

        public static AreaAnchor FirstOfKind(AnchorKind kind)
        {
            foreach (var a in AllAnchors) if (a.Kind == kind) return a;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ById.Clear();
            AllAnchors.Clear();
        }
    }
}
