using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DnWModLoader.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BepInConfig = BepInEx.Configuration;

namespace DnWModLoader.BepInExCompat
{
    internal sealed class BepInExSettingsSource : ISettingsSource
    {
        private readonly BepInConfig.ConfigFile _file;
        private readonly Dictionary<BepInConfig.ConfigDefinition, BepInExSettingEntry> _adapters = new Dictionary<BepInConfig.ConfigDefinition, BepInExSettingEntry>();
        private List<KeyValuePair<string, List<ConfigEntryBase>>> _sections = new List<KeyValuePair<string, List<ConfigEntryBase>>>();
        private int _builtFromCount = -1;

        public BepInExSettingsSource(BepInConfig.ConfigFile file)
        {
            _file = file;
        }

        public bool HasPendingChanges { get { return false; } }

        public bool HasEntries { get { return _file.Count > 0; } }

        public IList<KeyValuePair<string, List<ConfigEntryBase>>> EntriesBySection()
        {
            // Plugins can bind or remove settings at any time. Help.
            int count = _file.Count;
            if (count != _builtFromCount) Rebuild(count);
            return _sections;
        }

        private void Rebuild(int count)
        {
            var sections = new List<KeyValuePair<string, List<ConfigEntryBase>>>();
            var index = new Dictionary<string, List<ConfigEntryBase>>(StringComparer.Ordinal);
            var adapters = new Dictionary<BepInConfig.ConfigDefinition, BepInExSettingEntry>();
            foreach (var definition in _file.Keys)
            {
                if (!_adapters.TryGetValue(definition, out var adapter)) adapter = new BepInExSettingEntry(_file[definition]);
                adapter.BindIndex = adapters.Count;
                adapters[definition] = adapter;
                if (!index.TryGetValue(definition.Section, out var list))
                {
                    list = new List<ConfigEntryBase>();
                    index[definition.Section] = list;
                    sections.Add(new KeyValuePair<string, List<ConfigEntryBase>>(definition.Section, list));
                }
                list.Add(adapter);
            }
            foreach (var section in sections)
                section.Value.Sort((a, b) => a.Meta.Order != b.Meta.Order ? a.Meta.Order.CompareTo(b.Meta.Order) : a.BindIndex.CompareTo(b.BindIndex));

            _adapters.Clear();
            foreach (var pair in adapters) _adapters[pair.Key] = pair.Value;
            _sections = sections;
            _builtFromCount = count;
        }

        public SectionInfo GetSectionInfo(string section)
        {
            return null;
        }

        public void ResetSection(string section)
        {
            foreach (var group in EntriesBySection())
                if (group.Key == section)
                    foreach (var entry in group.Value) entry.Reset();
        }

        public void ResetAll()
        {
            foreach (var group in EntriesBySection())
                foreach (var entry in group.Value) entry.Reset();
        }

        public void Reload()
        {
            _file.Reload();
        }
    }

    internal sealed class BepInExSettingEntry : ConfigEntryBase
    {
        private readonly BepInConfig.ConfigEntryBase _entry;

        public BepInExSettingEntry(BepInConfig.ConfigEntryBase entry)
        {
            _entry = entry;
            Section = entry.Definition.Section;
            Key = entry.Definition.Key;
            Description = entry.Description?.Description;
            Meta = MetaFor(entry);
        }

        public override Type ValueType { get { return _entry.SettingType; } }

        public override object BoxedValue
        {
            get { return _entry.BoxedValue; }
            set { _entry.BoxedValue = Coerce(value, _entry.SettingType); }
        }

        public override object BoxedDefault { get { return _entry.DefaultValue; } }

        public override bool IsDefault { get { return Equals(_entry.BoxedValue, _entry.DefaultValue); } }

        public override void Reset()
        {
            _entry.BoxedValue = _entry.DefaultValue;
        }

        public override bool TrySetFromString(string text, out string error)
        {
            error = null;
            try
            {
                var type = _entry.SettingType;
                text = text ?? "";
                object parsed;
                if (type == typeof(string)) parsed = text;
                else if (type == typeof(bool)) parsed = ParseBool(text);
                else if (type.IsEnum) parsed = Enum.Parse(type, text.Trim(), true);
                else if (IsNumericType(type)) parsed = Convert.ChangeType(text.Trim(), type, CultureInfo.InvariantCulture);
                else parsed = BepInConfig.TomlTypeConverter.ConvertToValue(text.Trim(), type);
                _entry.BoxedValue = parsed;
                return true;
            }
            catch (Exception e)
            {
                error = (e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).Message;
                return false;
            }
        }

        public override string ValueToDisplayString()
        {
            object value = _entry.BoxedValue;
            if (value == null) return "";
            if (value is float f) return f.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString("0.####", CultureInfo.InvariantCulture);
            if (value is string || value is Enum) return value.ToString();
            if (value is IConvertible convertible) return convertible.ToString(CultureInfo.InvariantCulture);
            try { return BepInConfig.TomlTypeConverter.ConvertToString(value, _entry.SettingType); }
            catch { return value.ToString(); }
        }

        internal override bool TrySetFromToken(JToken token, out string error, out bool changed)
        {
            changed = false;
            error = "BepInEx settings are stored in their .cfg file";
            return false;
        }

        internal override JToken ValueToToken(JsonSerializer serializer) { return JValue.CreateNull(); }

        internal override JToken DefaultToToken(JsonSerializer serializer) { return JValue.CreateNull(); }

        private static bool ParseBool(string text)
        {
            string t = text.Trim().ToLowerInvariant();
            if (t == "1" || t == "on" || t == "yes") return true;
            if (t == "0" || t == "off" || t == "no") return false;
            return bool.Parse(t);
        }

        private static object Coerce(object value, Type type)
        {
            if (value == null || type.IsInstanceOfType(value)) return value;
            if (type.IsEnum) return value is string s ? Enum.Parse(type, s, true) : Enum.ToObject(type, value);
            if (value is IConvertible) return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            return value;
        }

        private static ConfigMeta MetaFor(BepInConfig.ConfigEntryBase entry)
        {
            var meta = new ConfigMeta();
            var acceptable = entry.Description?.AcceptableValues;
            if (acceptable != null)
            {
                var range = FindGeneric(acceptable.GetType(), typeof(BepInConfig.AcceptableValueRange<>));
                var list = FindGeneric(acceptable.GetType(), typeof(BepInConfig.AcceptableValueList<>));
                if (range != null && IsNumericType(entry.SettingType))
                {
                    meta.Min = Convert.ToDouble(range.GetProperty("MinValue").GetValue(acceptable, null), CultureInfo.InvariantCulture);
                    meta.Max = Convert.ToDouble(range.GetProperty("MaxValue").GetValue(acceptable, null), CultureInfo.InvariantCulture);
                }
                else if (list != null && list.GetProperty("AcceptableValues").GetValue(acceptable, null) is Array values)
                {
                    meta.AcceptableValues = values.Cast<object>().ToArray();
                }
            }
            if (entry.Description?.Tags != null)
                foreach (var tag in entry.Description.Tags)
                    if (tag != null && tag.GetType().Name == "ConfigurationManagerAttributes") ApplyManagerAttributes(meta, tag);
            return meta;
        }

        private static void ApplyManagerAttributes(ConfigMeta meta, object tag)
        {
            if (Read(tag, "Browsable") is bool browsable && !browsable) meta.Hidden = true;
            if (Read(tag, "IsAdvanced") is bool advanced && advanced) meta.Advanced = true;
            if (Read(tag, "Order") is int order) meta.Order = -order;
            if (Read(tag, "DispName") is string name && name.Length > 0) meta.DisplayName = name;
        }

        private static object Read(object target, string member)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = target.GetType();
            try
            {
                var field = type.GetField(member, flags);
                if (field != null) return field.GetValue(target);
                var property = type.GetProperty(member, flags);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
            }
            catch { }
            return null;
        }

        private static Type FindGeneric(Type type, Type genericDefinition)
        {
            for (; type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == genericDefinition) return type;
            return null;
        }
    }
}
