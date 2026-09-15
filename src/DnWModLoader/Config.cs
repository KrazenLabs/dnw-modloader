using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DnWModLoader.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace DnWModLoader.Config
{
    // Config UI settings
    // All of these are optional, just define the ones you need
    public sealed class ConfigMeta
    {
        // Label
        public string DisplayName { get; set; }
        // Minimum value for numbers
        public double? Min { get; set; }
        // Maximum value for numbers
        public double? Max { get; set; }
        // Slider stepping (0 = continuous for floats, 1 for integers)
        public double Step { get; set; }
        // Explicit list of allowed values
        public object[] AcceptableValues { get; set; }
        // Only shown when "advanced" is enabled in the Settings tab
        public bool Advanced { get; set; }
        // Not shown in settings (for example for internal use)
        public bool Hidden { get; set; }
        // Order of the entry within its section
        public int Order { get; set; }
        // Changing this entry only takes effect after a restart
        public bool RequiresRestart { get; set; }

        public static ConfigMeta Range(double min, double max, double step = 0) { return new ConfigMeta { Min = min, Max = max, Step = step }; }
        public static ConfigMeta Choice(params object[] values) { return new ConfigMeta { AcceptableValues = values }; }
        public static ConfigMeta AdvancedEntry() { return new ConfigMeta { Advanced = true }; }

        public bool HasRange { get { return Min.HasValue && Max.HasValue && Max.Value > Min.Value; } }
    }

    internal interface ISettingsSource
    {
        bool HasPendingChanges { get; }
        bool HasEntries { get; }
        IList<KeyValuePair<string, List<ConfigEntryBase>>> EntriesBySection();
        SectionInfo GetSectionInfo(string section);
        void ResetSection(string section);
        void ResetAll();
        void Reload();
    }

    // Config sections
    public sealed class SectionInfo
    {
        public string Section { get; internal set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int Order { get; set; }
        public bool Advanced { get; set; }
    }

    // Config entries
    public abstract class ConfigEntryBase
    {
        public string Section { get; internal set; }
        public string Key { get; internal set; }
        public string Description { get; internal set; }
        // Assign config UI settings
        public ConfigMeta Meta { get; internal set; }
        public abstract Type ValueType { get; }
        public abstract object BoxedValue { get; set; }
        public abstract object BoxedDefault { get; }
        // Actual config
        public ModConfig Owner { get; internal set; }
        internal int BindIndex { get; set; }

        public string DisplayName { get { return string.IsNullOrEmpty(Meta.DisplayName) ? Key : Meta.DisplayName; } }

        public abstract bool IsDefault { get; }

        internal abstract bool TrySetFromToken(JToken token, out string error);
        internal abstract JToken ValueToToken(JsonSerializer serializer);
        internal abstract JToken DefaultToToken(JsonSerializer serializer);

        // Reset to default value
        public abstract void Reset();

        // Parses user input and tries to apply
        public abstract bool TrySetFromString(string text, out string error);

        // The current value as editable text
        public abstract string ValueToDisplayString();

        internal static bool IsNumericType(Type t)
        {
            return t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte) || t == typeof(decimal);
        }

        internal static bool IsIntegerType(Type t)
        {
            return t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);
        }
    }

    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        private T _value;

        public T DefaultValue { get; }

        // Current value
        public T Value
        {
            get { return _value; }
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value;
                RaiseChanged();
            }
        }

        // Called when value changes
        public event Action<T> Changed;

        internal ConfigEntry(string section, string key, T defaultValue, string description, ConfigMeta meta)
        {
            Section = section;
            Key = key;
            Description = description;
            Meta = meta ?? new ConfigMeta();
            DefaultValue = defaultValue;
            _value = defaultValue;
        }

        public override Type ValueType { get { return typeof(T); } }

        public override object BoxedValue
        {
            get { return _value; }
            set { Value = ConvertBoxed(value); }
        }

        public override object BoxedDefault { get { return DefaultValue; } }

        public override bool IsDefault { get { return EqualityComparer<T>.Default.Equals(_value, DefaultValue); } }

        public override void Reset() { Value = DefaultValue; }

        public static implicit operator T(ConfigEntry<T> entry) { return entry.Value; }

        private static T ConvertBoxed(object value)
        {
            if (value is T typed) return typed;
            if (value == null) return default(T);
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (target.IsEnum)
            {
                if (value is string s) return (T)Enum.Parse(target, s, true);
                return (T)Enum.ToObject(target, value);
            }
            if (value is IConvertible) return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            if (value is JToken token) return token.ToObject<T>(ModConfig.Serializer);
            return JToken.FromObject(value, ModConfig.Serializer).ToObject<T>(ModConfig.Serializer);
        }

        public override bool TrySetFromString(string text, out string error)
        {
            error = null;
            try
            {
                var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                object parsed;
                text = text ?? "";
                if (target == typeof(string)) parsed = text;
                else if (target == typeof(bool))
                {
                    string t = text.Trim().ToLowerInvariant();
                    if (t == "1" || t == "on" || t == "yes") parsed = true;
                    else if (t == "0" || t == "off" || t == "no") parsed = false;
                    else parsed = bool.Parse(t);
                }
                else if (target.IsEnum) parsed = Enum.Parse(target, text.Trim(), true);
                else if (IsNumericType(target)) parsed = Convert.ChangeType(text.Trim(), target, CultureInfo.InvariantCulture);
                else parsed = JsonConvert.DeserializeObject(text, target, ModConfig.SerializerSettings);
                Value = ConvertBoxed(parsed);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public override string ValueToDisplayString()
        {
            object v = _value;
            if (v == null) return "";
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (target == typeof(string) || target.IsEnum) return v.ToString();
            if (target == typeof(float)) return ((float)v).ToString("0.###", CultureInfo.InvariantCulture);
            if (target == typeof(double)) return ((double)v).ToString("0.####", CultureInfo.InvariantCulture);
            if (v is IConvertible c) return c.ToString(CultureInfo.InvariantCulture);
            return JsonConvert.SerializeObject(v, Formatting.None, ModConfig.SerializerSettings);
        }

        private void RaiseChanged()
        {
            try { Changed?.Invoke(_value); }
            catch (Exception e) { Owner?.Logger?.Exception(e, "Config change handler for " + Section + "." + Key + " threw"); }
            Owner?.NotifyChanged(this);
        }

        internal override bool TrySetFromToken(JToken token, out string error)
        {
            error = null;
            try
            {
                T parsed;
                if (token == null || token.Type == JTokenType.Null)
                {
                    parsed = DefaultValue;
                }
                else if (typeof(T).IsEnum && token.Type == JTokenType.String)
                {
                    parsed = (T)Enum.Parse(typeof(T), (string)token, true);
                }
                else
                {
                    parsed = token.ToObject<T>(ModConfig.Serializer);
                }
                if (!EqualityComparer<T>.Default.Equals(_value, parsed))
                {
                    _value = parsed;
                    try { Changed?.Invoke(_value); }
                    catch (Exception e) { Owner?.Logger?.Exception(e, "Config change handler for " + Section + "." + Key + " threw"); }
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        internal override JToken ValueToToken(JsonSerializer serializer) { return _value == null ? JValue.CreateNull() : JToken.FromObject(_value, serializer); }
        internal override JToken DefaultToToken(JsonSerializer serializer) { return DefaultValue == null ? JValue.CreateNull() : JToken.FromObject(DefaultValue, serializer); }
    }

    // Per mod config
    public sealed class ModConfig : ISettingsSource
    {
        internal static readonly JsonSerializerSettings SerializerSettings = CreateSettings();
        internal static readonly JsonSerializer Serializer = JsonSerializer.Create(SerializerSettings);
        private static readonly List<ModConfig> Registry = new List<ModConfig>();
        private static readonly object RegistrySync = new object();
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(0.5);

        private readonly object _sync = new object();
        private readonly Dictionary<string, ConfigEntryBase> _entries = new Dictionary<string, ConfigEntryBase>(StringComparer.Ordinal);
        private readonly List<ConfigEntryBase> _ordered = new List<ConfigEntryBase>();
        private readonly Dictionary<string, SectionInfo> _sections = new Dictionary<string, SectionInfo>(StringComparer.Ordinal);
        private JObject _document = new JObject();
        private bool _suppressSave;
        private bool _dirty;
        private DateTime _dirtyAt;

        public string FilePath { get; }
        // ID of your mod
        public string OwnerId { get; }
        internal ModLogger Logger { get; }

        // Automatically save config if changed
        public bool SaveOnChange { get; set; } = true;

        public event Action<ConfigEntryBase> SettingChanged;

        /// List of all config entries
        public IReadOnlyList<ConfigEntryBase> Entries { get { lock (_sync) return _ordered.ToArray(); } }

        public bool HasPendingChanges { get { return _dirty; } }

        bool ISettingsSource.HasEntries { get { lock (_sync) return _ordered.Count > 0; } }

        internal ModConfig(string filePath, ModLogger logger)
        {
            FilePath = filePath;
            OwnerId = Path.GetFileNameWithoutExtension(filePath);
            Logger = logger;
            LoadDocument();
            lock (RegistrySync) Registry.Add(this);
        }

        private static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }

        public static IReadOnlyList<ModConfig> All { get { lock (RegistrySync) return Registry.ToArray(); } }

        // Writes pending config changes
        public static void FlushPending()
        {
            ModConfig[] configs;
            lock (RegistrySync) configs = Registry.ToArray();
            var now = DateTime.UtcNow;
            foreach (var config in configs)
            {
                if (config._dirty && now - config._dirtyAt >= SaveDelay) config.Save();
            }
        }

        // Writes all config changes immediately
        public static void FlushAll()
        {
            ModConfig[] configs;
            lock (RegistrySync) configs = Registry.ToArray();
            foreach (var config in configs) if (config._dirty) config.Save();
        }

        // Registers a new setting or returns it if already registered
        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description = null)
        {
            return Bind(section, key, defaultValue, description, null);
        }

        /// Registers a setting with a UI config
        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description, ConfigMeta meta)
        {
            if (string.IsNullOrEmpty(section)) throw new ArgumentException("section must not be empty", nameof(section));
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key must not be empty", nameof(key));

            lock (_sync)
            {
                string id = section + "/" + key;
                if (_entries.TryGetValue(id, out var existing))
                {
                    if (existing is ConfigEntry<T> typed)
                    {
                        if (meta != null) typed.Meta = meta;
                        if (!string.IsNullOrEmpty(description)) typed.Description = description;
                        return typed;
                    }
                    throw new InvalidOperationException("Config entry " + id + " is already bound with type " + existing.ValueType.Name);
                }

                var entry = new ConfigEntry<T>(section, key, defaultValue, description, meta) { Owner = this, BindIndex = _ordered.Count };
                _entries[id] = entry;
                _ordered.Add(entry);

                bool needsWrite = true;
                var token = GetValueToken(section, key);
                if (token != null)
                {
                    if (entry.TrySetFromToken(token, out string error)) needsWrite = false;
                    else Logger?.Warning("Config " + id + " has an invalid value (" + error + "); using default " + defaultValue);
                }

                WriteEntryToDocument(entry);
                if (needsWrite && !_suppressSave) MarkDirty();
                return entry;
            }
        }

        // Registers numeric setting shown as a slider
        public ConfigEntry<T> BindRange<T>(string section, string key, T defaultValue, double min, double max, string description = null, double step = 0)
        {
            return Bind(section, key, defaultValue, description, ConfigMeta.Range(min, max, step));
        }

        // Sets display name and description for a section
        public SectionInfo DescribeSection(string section, string displayName, string description = null, int order = 0, bool advanced = false)
        {
            lock (_sync)
            {
                if (!_sections.TryGetValue(section, out var info))
                {
                    info = new SectionInfo { Section = section };
                    _sections[section] = info;
                }
                info.DisplayName = displayName;
                info.Description = description;
                info.Order = order;
                info.Advanced = advanced;
                return info;
            }
        }

        public SectionInfo GetSectionInfo(string section)
        {
            lock (_sync) return _sections.TryGetValue(section, out var info) ? info : null;
        }

        public bool TryGetEntry(string section, string key, out ConfigEntryBase entry)
        {
            lock (_sync) return _entries.TryGetValue(section + "/" + key, out entry);
        }

        public IList<KeyValuePair<string, List<ConfigEntryBase>>> EntriesBySection()
        {
            lock (_sync)
            {
                var groups = new List<KeyValuePair<string, List<ConfigEntryBase>>>();
                var index = new Dictionary<string, List<ConfigEntryBase>>(StringComparer.Ordinal);
                foreach (var entry in _ordered)
                {
                    if (!index.TryGetValue(entry.Section, out var list))
                    {
                        list = new List<ConfigEntryBase>();
                        index[entry.Section] = list;
                        groups.Add(new KeyValuePair<string, List<ConfigEntryBase>>(entry.Section, list));
                    }
                    list.Add(entry);
                }
                foreach (var group in groups)
                    group.Value.Sort((a, b) => a.Meta.Order != b.Meta.Order ? a.Meta.Order.CompareTo(b.Meta.Order) : a.BindIndex.CompareTo(b.BindIndex));
                var ordered = groups.Select((g, i) => new { g, i, order = _sections.TryGetValue(g.Key, out var info) ? info.Order : 0 })
                                    .OrderBy(x => x.order).ThenBy(x => x.i).Select(x => x.g).ToList();
                return ordered;
            }
        }

        // Reset section to default values
        public void ResetSection(string section)
        {
            foreach (var entry in Entries) if (entry.Section == section) entry.Reset();
        }

        // Reset all entries to default values
        public void ResetAll()
        {
            foreach (var entry in Entries) entry.Reset();
        }

        public void Save()
        {
            lock (_sync)
            {
                _dirty = false;
                try
                {
                    foreach (var entry in _ordered) WriteEntryToDocument(entry);
                    string dir = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, _document.ToString(Formatting.Indented), new UTF8Encoding(false));
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                    File.Move(tmp, FilePath);
                }
                catch (Exception e)
                {
                    Logger?.Exception(e, "Failed to save config " + FilePath);
                }
            }
        }

        // Reloads the config and applies all values
        public void Reload()
        {
            lock (_sync)
            {
                LoadDocument();
                _suppressSave = true;
                try
                {
                    foreach (var entry in _ordered)
                    {
                        var token = GetValueToken(entry.Section, entry.Key);
                        if (token == null) continue;
                        if (!entry.TrySetFromToken(token, out string error))
                            Logger?.Warning("Config " + entry.Section + "/" + entry.Key + " has an invalid value (" + error + "); keeping " + entry.BoxedValue);
                    }
                }
                finally { _suppressSave = false; }
            }
        }

        internal void NotifyChanged(ConfigEntryBase entry)
        {
            try { SettingChanged?.Invoke(entry); }
            catch (Exception e) { Logger?.Exception(e, "SettingChanged handler threw"); }
            if (SaveOnChange && !_suppressSave) MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _dirtyAt = DateTime.UtcNow;
        }

        private void LoadDocument()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var parsed = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(FilePath));
                    _document = parsed ?? new JObject();
                    return;
                }
            }
            catch (Exception e)
            {
                Logger?.Warning("Config file " + FilePath + " could not be parsed (" + e.Message + "); it will be rewritten with defaults.");
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
            }
            _document = new JObject();
        }

        private JToken GetValueToken(string section, string key)
        {
            if (!(_document[section] is JObject sectionObject)) return null;
            var keyToken = sectionObject[key];
            if (keyToken == null) return null;
            // Supports both { "value": x } and a bare value
            if (keyToken is JObject keyObject && keyObject.ContainsKey("value")) return keyObject["value"];
            return keyToken;
        }

        private void WriteEntryToDocument(ConfigEntryBase entry)
        {
            if (!(_document[entry.Section] is JObject sectionObject))
            {
                sectionObject = new JObject();
                _document[entry.Section] = sectionObject;
            }
            var keyObject = new JObject
            {
                ["value"] = entry.ValueToToken(Serializer),
                ["default"] = entry.DefaultToToken(Serializer),
            };
            if (!string.IsNullOrEmpty(entry.Description)) keyObject["description"] = entry.Description;
            if (entry.Meta.HasRange) keyObject["range"] = new JArray(entry.Meta.Min.Value, entry.Meta.Max.Value);
            if (entry.Meta.AcceptableValues != null && entry.Meta.AcceptableValues.Length > 0) keyObject["choices"] = JArray.FromObject(entry.Meta.AcceptableValues, Serializer);
            sectionObject[entry.Key] = keyObject;
        }
    }
}
