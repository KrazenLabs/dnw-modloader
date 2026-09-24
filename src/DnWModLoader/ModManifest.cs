using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DnWModLoader
{
    public static class VersionUtil
    {
        private static readonly Regex Numeric = new Regex(@"^\d+", RegexOptions.Compiled);
        private static readonly char[] Separators = { '-', '+', ' ' };

        // Parses version strings
        public static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
            int dash = text.IndexOfAny(Separators);
            if (dash >= 0) text = text.Substring(0, dash);
            var parts = text.Split('.');
            var numbers = new List<int>();
            foreach (var part in parts)
            {
                var m = Numeric.Match(part);
                if (!m.Success) break;
                if (!int.TryParse(m.Value, out int n)) break;
                numbers.Add(n);
                if (numbers.Count == 4) break;
            }
            if (numbers.Count == 0) return false;
            while (numbers.Count < 2) numbers.Add(0);
            version = numbers.Count == 2 ? new Version(numbers[0], numbers[1])
                    : numbers.Count == 3 ? new Version(numbers[0], numbers[1], numbers[2])
                    : new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        public static Version ParseOrDefault(string text, Version fallback = null)
        {
            return TryParse(text, out var v) ? v : (fallback ?? new Version(0, 0));
        }

        public static int Compare(Version a, Version b)
        {
            int c = Clamp(a.Major).CompareTo(Clamp(b.Major)); if (c != 0) return c;
            c = Clamp(a.Minor).CompareTo(Clamp(b.Minor)); if (c != 0) return c;
            c = Clamp(a.Build).CompareTo(Clamp(b.Build)); if (c != 0) return c;
            return Clamp(a.Revision).CompareTo(Clamp(b.Revision));
        }

        private static int Clamp(int x) { return x < 0 ? 0 : x; }
    }

    public sealed class VersionConstraint
    {
        private static readonly string[] Operators = { ">=", "<=", "==", ">", "<", "=", "^", "~" };

        private readonly string _op;
        private readonly Version _version;

        public string Text { get; }

        private VersionConstraint(string text, string op, Version version)
        {
            Text = text;
            _op = op;
            _version = version;
        }

        public static VersionConstraint Any { get; } = new VersionConstraint("*", "*", null);

        public static bool TryParse(string text, out VersionConstraint constraint)
        {
            constraint = null;
            if (string.IsNullOrEmpty(text) || text.Trim() == "*") { constraint = Any; return true; }
            string t = text.Trim();
            string op = ">=";
            foreach (var candidate in Operators)
            {
                if (t.StartsWith(candidate, StringComparison.Ordinal))
                {
                    op = candidate == "==" ? "=" : candidate;
                    t = t.Substring(candidate.Length).Trim();
                    break;
                }
            }
            if (!VersionUtil.TryParse(t, out var version)) return false;
            constraint = new VersionConstraint(text, op, version);
            return true;
        }

        public bool Satisfies(Version actual)
        {
            if (_version == null) return true;
            if (actual == null) return false;
            int c = VersionUtil.Compare(actual, _version);
            switch (_op)
            {
                case ">=": return c >= 0;
                case ">": return c > 0;
                case "<=": return c <= 0;
                case "<": return c < 0;
                case "=": return c == 0;
                case "^": return c >= 0 && actual.Major == _version.Major;
                case "~": return c >= 0 && actual.Major == _version.Major && actual.Minor == _version.Minor;
                default: return true;
            }
        }

        public override string ToString() { return Text; }
    }

    [JsonConverter(typeof(ModDependencyConverter))]
    public sealed class ModDependency
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public bool Optional { get; set; }

        public override string ToString()
        {
            return Id + (string.IsNullOrEmpty(Version) ? "" : " " + Version) + (Optional ? " (optional)" : "");
        }
    }

    internal sealed class ModDependencyConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) { return objectType == typeof(ModDependency); }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type == JTokenType.String) return new ModDependency { Id = (string)token };
            if (token is JObject obj)
            {
                return new ModDependency
                {
                    Id = (string)obj["id"],
                    Version = (string)obj["version"],
                    Optional = obj["optional"] != null && obj["optional"].Type == JTokenType.Boolean && (bool)obj["optional"],
                };
            }
            throw new JsonSerializationException("A dependency must be a string or an object with an id.");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var dep = (ModDependency)value;
            writer.WriteStartObject();
            writer.WritePropertyName("id"); writer.WriteValue(dep.Id);
            if (!string.IsNullOrEmpty(dep.Version)) { writer.WritePropertyName("version"); writer.WriteValue(dep.Version); }
            if (dep.Optional) { writer.WritePropertyName("optional"); writer.WriteValue(true); }
            writer.WriteEndObject();
        }
    }

    // mod.json
    public sealed class ModManifest
    {
        [JsonProperty("id")] public string Id { get; set; }

        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("version")] public string Version { get; set; }

        [JsonProperty("author")] public string Author { get; set; }

        [JsonProperty("description")] public string Description { get; set; }

        [JsonProperty("assembly")] public string Assembly { get; set; }

        [JsonProperty("entryType")] public string EntryType { get; set; }

        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;

        [JsonProperty("dependencies")] public List<ModDependency> Dependencies { get; set; } = new List<ModDependency>();

        [JsonProperty("loadAfter")] public List<string> LoadAfter { get; set; } = new List<string>();

        [JsonProperty("loadBefore")] public List<string> LoadBefore { get; set; } = new List<string>();

        [JsonProperty("loaderVersion")] public string LoaderVersion { get; set; }

        // Optional
        [JsonProperty("url")] public string Url { get; set; }

        private static readonly Regex IdPattern = new Regex(@"^[a-z0-9][a-z0-9._\-]{0,127}$", RegexOptions.Compiled);

        public static bool IsValidId(string id) { return !string.IsNullOrEmpty(id) && IdPattern.IsMatch(id); }

        public static ModManifest Parse(string json)
        {
            var manifest = JsonConvert.DeserializeObject<ModManifest>(json) ?? new ModManifest();
            manifest.Dependencies = manifest.Dependencies ?? new List<ModDependency>();
            manifest.LoadAfter = manifest.LoadAfter ?? new List<string>();
            manifest.LoadBefore = manifest.LoadBefore ?? new List<string>();
            return manifest;
        }
    }
}
