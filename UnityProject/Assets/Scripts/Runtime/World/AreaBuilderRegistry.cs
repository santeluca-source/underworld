using System;
using System.Collections.Generic;
using System.Reflection;
using Underworld.Core;
using UnityEngine;

namespace Underworld.World
{
    /// <summary>
    /// Costruttore di un'area: crea il mondo a runtime con le funzioni di <see cref="BuildContext"/>.
    /// Regola: usa solo ctx.Rng per i numeri casuali e non leggere lo stato del gioco,
    /// così server e client ottengono lo stesso mondo.
    /// </summary>
    public interface IAreaBuilder
    {
        void Build(BuildContext ctx);
    }

    /// <summary>
    /// Segna una classe <see cref="IAreaBuilder"/> come costruttore di un'area.
    /// Il registro la trova da solo: per aggiungere un'area basta una classe nuova, nessun file da modificare.
    /// <code>[AreaBuilderFor(AreaId.Industrial, Seed = 2024)] public sealed class IndustrialBuilder : IAreaBuilder { ... }</code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AreaBuilderForAttribute : Attribute
    {
        public AreaId Area { get; }

        /// <summary>Seme standard dell'area (uguale su server e client).</summary>
        public int Seed { get; set; } = 1;

        public AreaBuilderForAttribute(AreaId area) => Area = area;
    }

    /// <summary>Elenco dei costruttori di area (trovati con l'attributo o registrati a mano).</summary>
    public static class AreaBuilderRegistry
    {
        readonly struct Entry
        {
            public readonly Func<IAreaBuilder> Factory;
            public readonly int Seed;
            public readonly string Source;

            public Entry(Func<IAreaBuilder> factory, int seed, string source)
            {
                Factory = factory;
                Seed = seed;
                Source = source;
            }
        }

        static Dictionary<AreaId, Entry> _scanned;
        static readonly Dictionary<AreaId, Entry> Manual = new Dictionary<AreaId, Entry>();

        /// <summary>Registra a mano un costruttore (vince su quello trovato con l'attributo).</summary>
        public static void Register(AreaId area, Func<IAreaBuilder> factory, int seed)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            Manual[area] = new Entry(factory, seed, "manuale");
        }

        public static void Unregister(AreaId area) => Manual.Remove(area);

        public static bool Has(AreaId area) => Manual.ContainsKey(area) || Scanned().ContainsKey(area);

        /// <summary>Aree che hanno un costruttore.</summary>
        public static List<AreaId> Registered()
        {
            var list = new List<AreaId>(Scanned().Keys);
            foreach (var k in Manual.Keys) if (!list.Contains(k)) list.Add(k);
            list.Sort();
            return list;
        }

        /// <summary>Crea il costruttore dell'area e dà il suo seme standard.</summary>
        public static bool TryCreate(AreaId area, out IAreaBuilder builder, out int defaultSeed)
        {
            if (Manual.TryGetValue(area, out var e) || Scanned().TryGetValue(area, out e))
            {
                builder = e.Factory();
                defaultSeed = e.Seed;
                return builder != null;
            }
            builder = null;
            defaultSeed = 0;
            return false;
        }

        static Dictionary<AreaId, Entry> Scanned()
        {
            if (_scanned != null) return _scanned;
            _scanned = new Dictionary<AreaId, Entry>();
            var found = new List<(Type type, AreaBuilderForAttribute attr)>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (SkipAssembly(asm.GetName().Name)) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface || !typeof(IAreaBuilder).IsAssignableFrom(t)) continue;
                    var attr = t.GetCustomAttribute<AreaBuilderForAttribute>(false);
                    if (attr == null) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null)
                    {
                        Debug.LogWarning($"[World] {t.FullName}: serve un costruttore senza parametri.");
                        continue;
                    }
                    found.Add((t, attr));
                }
            }
            // Ordine fisso (per nome), così un doppione dà sempre lo stesso risultato.
            found.Sort((a, b) => string.CompareOrdinal(a.type.FullName, b.type.FullName));
            foreach (var (type, attr) in found)
            {
                if (_scanned.TryGetValue(attr.Area, out var old))
                {
                    Debug.LogWarning($"[World] Due costruttori per {attr.Area}: tengo {old.Source}, ignoro {type.FullName}.");
                    continue;
                }
                var t = type;
                _scanned.Add(attr.Area, new Entry(() => (IAreaBuilder)Activator.CreateInstance(t), attr.Seed, t.FullName));
            }
            return _scanned;
        }

        static bool SkipAssembly(string name)
        {
            return name.StartsWith("System", StringComparison.Ordinal)
                || name.StartsWith("Unity.", StringComparison.Ordinal)
                || name.StartsWith("UnityEngine", StringComparison.Ordinal)
                || name.StartsWith("UnityEditor", StringComparison.Ordinal)
                || name.StartsWith("Mono.", StringComparison.Ordinal)
                || name == "mscorlib" || name == "netstandard" || name.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Newtonsoft", StringComparison.Ordinal) || name.StartsWith("Bee.", StringComparison.Ordinal)
                || name.StartsWith("JetBrains", StringComparison.Ordinal) || name.StartsWith("Microsoft", StringComparison.Ordinal);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _scanned = null;
            Manual.Clear();
        }
    }
}
