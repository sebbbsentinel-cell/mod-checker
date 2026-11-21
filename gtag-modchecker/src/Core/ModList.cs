using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FanGame.ModChecker
{
    public enum MatchType { Exact, Contains, Prefix, Suffix, Wildcard }
    public enum MatchTarget { Key, Value, Any }
    /// <summary>cheat = juks (teller mot score), mod = kjent lovlig mod (ALLOWED MODS), unsure = usikker.</summary>
    public enum ModCategory { Cheat, Mod, Unsure }

    public sealed class ModEntry
    {
        public string Pattern { get; set; }
        public MatchType MatchType { get; set; } = MatchType.Exact;
        public MatchTarget Target { get; set; } = MatchTarget.Key;
        public string Label { get; set; }
        public ModCategory Category { get; set; } = ModCategory.Unsure;
        public float Confidence { get; set; } = 0.5f;
        /// <summary>Valgfri kildehenvisning (vises ikke, kun dokumentasjon).</summary>
        public string Source { get; set; }

        internal Regex WildcardRegex;

        internal bool Matches(string text)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(Pattern)) return false;
            switch (MatchType)
            {
                case MatchType.Exact: return string.Equals(text, Pattern, StringComparison.OrdinalIgnoreCase);
                case MatchType.Contains: return text.IndexOf(Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                case MatchType.Prefix: return text.StartsWith(Pattern, StringComparison.OrdinalIgnoreCase);
                case MatchType.Suffix: return text.EndsWith(Pattern, StringComparison.OrdinalIgnoreCase);
                case MatchType.Wildcard:
                    if (WildcardRegex == null)
                        WildcardRegex = new Regex("^" + Regex.Escape(Pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    return WildcardRegex.IsMatch(text);
                default: return false;
            }
        }
    }

    public sealed class AllowEntry
    {
        public string Pattern { get; set; }
        public MatchType MatchType { get; set; } = MatchType.Exact;
        internal ModEntry AsEntry() => new ModEntry { Pattern = Pattern, MatchType = MatchType };
    }

    /// <summary>Modliste i JSON (samme format som mods.json). Parses med innebygd MiniJson, ingen ekstra DLL-er.</summary>
    public sealed class ModList
    {
        public int Version { get; set; }
        public List<ModEntry> Entries { get; set; } = new List<ModEntry>();
        public List<AllowEntry> Allowlist { get; set; } = new List<AllowEntry>();
        public List<string> AllowedRpcs { get; set; } = new List<string>();

        private List<ModEntry> _allow;
        private HashSet<string> _rpcs;

        public static ModList FromJson(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>
                       ?? throw new FormatException("Rotelementet i modlista må være et JSON-objekt { ... }");
            var list = new ModList { Version = (int)Num(root, "version", 0) };

            foreach (var o in Arr(root, "entries"))
            {
                if (!(o is Dictionary<string, object> d)) continue;
                list.Entries.Add(new ModEntry
                {
                    Pattern = Str(d, "pattern"),
                    MatchType = Enum<MatchType>(d, "matchType", MatchType.Exact),
                    Target = Enum<MatchTarget>(d, "target", MatchTarget.Key),
                    Label = Str(d, "label"),
                    Category = Enum<ModCategory>(d, "category", ModCategory.Unsure),
                    Confidence = (float)Num(d, "confidence", 0.5),
                    Source = Str(d, "source")
                });
            }
            foreach (var o in Arr(root, "allowlist"))
            {
                if (o is string s) list.Allowlist.Add(new AllowEntry { Pattern = s });
                else if (o is Dictionary<string, object> d)
                    list.Allowlist.Add(new AllowEntry { Pattern = Str(d, "pattern"), MatchType = Enum<MatchType>(d, "matchType", MatchType.Exact) });
            }
            foreach (var o in Arr(root, "allowedRpcs"))
                if (o is string s) list.AllowedRpcs.Add(s);

            list.Prepare();
            return list;
        }

        public static ModList LoadFromFile(string path) => FromJson(File.ReadAllText(path));

        /// <summary>Laster mods.json som er bygd inn i DLL-en (EmbeddedResource "FanGame.ModChecker.mods.json").</summary>
        public static ModList LoadEmbeddedDefault()
        {
            string json = ReadEmbeddedDefaultJson();
            return json == null ? new ModList() : FromJson(json);
        }

        public static string ReadEmbeddedDefaultJson()
        {
            var asm = typeof(ModList).Assembly;
            using (var s = asm.GetManifestResourceStream("FanGame.ModChecker.mods.json"))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        public string ToJson()
        {
            string Lower<T>(T e) => e.ToString().ToLowerInvariant();
            var root = new Dictionary<string, object>
            {
                ["version"] = Version,
                ["entries"] = Entries.Select(e =>
                {
                    var d = new Dictionary<string, object>
                    {
                        ["pattern"] = e.Pattern, ["matchType"] = Lower(e.MatchType), ["target"] = Lower(e.Target),
                        ["label"] = e.Label, ["category"] = Lower(e.Category), ["confidence"] = e.Confidence
                    };
                    if (e.Source != null) d["source"] = e.Source;
                    return (object)d;
                }).ToList(),
                ["allowlist"] = Allowlist.Select(a => (object)new Dictionary<string, object> { ["pattern"] = a.Pattern, ["matchType"] = Lower(a.MatchType) }).ToList(),
                ["allowedRpcs"] = AllowedRpcs.Cast<object>().ToList()
            };
            return MiniJson.Serialize(root);
        }

        private void Prepare()
        {
            Entries = (Entries ?? new List<ModEntry>()).Where(e => !string.IsNullOrEmpty(e.Pattern)).ToList();
            foreach (var e in Entries) e.Confidence = Math.Max(0f, Math.Min(1f, e.Confidence));
            _allow = (Allowlist ?? new List<AllowEntry>()).Where(a => !string.IsNullOrEmpty(a.Pattern)).Select(a => a.AsEntry()).ToList();
            _rpcs = new HashSet<string>(AllowedRpcs ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public bool IsAllowed(string key)
        {
            if (_allow == null) Prepare();
            return _allow.Any(a => a.Matches(key));
        }

        public bool HasRpcAllowlist { get { if (_rpcs == null) Prepare(); return _rpcs.Count > 0; } }
        public bool IsRpcAllowed(string name) { if (_rpcs == null) Prepare(); return _rpcs.Contains(name ?? ""); }

        public IEnumerable<ModEntry> Match(string key, string value)
        {
            foreach (var e in Entries)
            {
                bool hit;
                switch (e.Target)
                {
                    case MatchTarget.Key: hit = e.Matches(key); break;
                    case MatchTarget.Value: hit = e.Matches(value); break;
                    default: hit = e.Matches(key) || e.Matches(value); break;
                }
                if (hit) yield return e;
            }
        }

        // ------------------------------------------------------------ hjelpere
        private static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        private static double Num(Dictionary<string, object> d, string k, double def) =>
            d.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : def;
        private static IEnumerable<object> Arr(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) && v is List<object> l ? l : Enumerable.Empty<object>();
        private static T Enum<T>(Dictionary<string, object> d, string k, T def) where T : struct =>
            d.TryGetValue(k, out var v) && v is string s && System.Enum.TryParse(s, true, out T r) ? r : def;
    }
}
