using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private BepInConfig.ConfigEntryBase[] _builtFrom;
        private bool _dirty;

        public BepInExSettingsSource(BepInConfig.ConfigFile file)
        {
            _file = file;
        }

        public bool HasPendingChanges { get { return _dirty; } }

        public bool HasEntries { get { return _file.Count > 0; } }

        public IList<KeyValuePair<string, List<ConfigEntryBase>>> EntriesBySection()
        {
            // Plugins can bind or remove settings at any time. Help.
            var entries = ((IDictionary<BepInConfig.ConfigDefinition, BepInConfig.ConfigEntryBase>)_file).Values.ToArray();
            if (_builtFrom == null || !entries.SequenceEqual(_builtFrom)) Rebuild(entries);
            return _sections;
        }

        private void Rebuild(BepInConfig.ConfigEntryBase[] entries)
        {
            var sections = new List<KeyValuePair<string, List<ConfigEntryBase>>>();
            var index = new Dictionary<string, List<ConfigEntryBase>>(StringComparer.Ordinal);
            var adapters = new Dictionary<BepInConfig.ConfigDefinition, BepInExSettingEntry>();
            foreach (var entry in entries)
            {
                var definition = entry.Definition;
                if (!_adapters.TryGetValue(definition, out var adapter) || !adapter.Wraps(entry)) adapter = new BepInExSettingEntry(this, entry);
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
            _builtFrom = entries;
        }

        public SectionInfo GetSectionInfo(string section)
        {
            return null;
        }

        public void Reload()
        {
            if (!File.Exists(_file.ConfigFilePath)) return;
            bool changed = false;
            EventHandler<BepInConfig.SettingChangedEventArgs> onChanged = (sender, args) => changed = true;
            bool saveOnSet = _file.SaveOnConfigSet;
            _file.SettingChanged += onChanged;
            _file.SaveOnConfigSet = false;
            try { _file.Reload(); }
            finally
            {
                _file.SaveOnConfigSet = saveOnSet;
                _file.SettingChanged -= onChanged;
            }
            if (saveOnSet && (changed || _dirty)) SaveNow();
        }

        internal void Apply(Action assign)
        {
            bool changed = false;
            EventHandler<BepInConfig.SettingChangedEventArgs> onChanged = (sender, args) => changed = true;
            bool saveOnSet = _file.SaveOnConfigSet;
            _file.SettingChanged += onChanged;
            _file.SaveOnConfigSet = false;
            try { assign(); }
            finally
            {
                _file.SaveOnConfigSet = saveOnSet;
                _file.SettingChanged -= onChanged;
            }
            if (saveOnSet && changed) ScheduleSave();
        }

        private void ScheduleSave()
        {
            _dirty = true;
            DeferredSaves.Schedule(this, SaveNow);
        }

        private void SaveNow()
        {
            _dirty = false;
            DeferredSaves.Cancel(this);
            _file.Save();
        }
    }

    internal sealed class BepInExSettingEntry : ConfigEntryBase
    {
        private readonly BepInExSettingsSource _source;
        private readonly BepInConfig.ConfigEntryBase _entry;

        public BepInExSettingEntry(BepInExSettingsSource source, BepInConfig.ConfigEntryBase entry)
        {
            _source = source;
            _entry = entry;
            Section = entry.Definition.Section;
            Key = entry.Definition.Key;
            Description = entry.Description?.Description;
            Meta = MetaFor(entry);
        }

        internal bool Wraps(BepInConfig.ConfigEntryBase entry) { return ReferenceEquals(_entry, entry); }

        public override Type ValueType { get { return _entry.SettingType; } }

        public override object BoxedValue
        {
            get { return _entry.BoxedValue; }
            set { _source.Apply(() => _entry.BoxedValue = SettingValues.Coerce(value, _entry.SettingType)); }
        }

        public override object BoxedDefault { get { return _entry.DefaultValue; } }

        public override bool IsDefault { get { return Equals(_entry.BoxedValue, _entry.DefaultValue); } }

        public override void Reset()
        {
            _source.Apply(() => _entry.BoxedValue = _entry.DefaultValue);
        }

        public override bool TrySetFromString(string text, out string error)
        {
            var type = _entry.SettingType;
            object parsed;
            if (!SettingValues.TryParse(text, type, t => BepInConfig.TomlTypeConverter.ConvertToValue(t.Trim(), type), out parsed, out error)) return false;
            try
            {
                _source.Apply(() => _entry.BoxedValue = parsed);
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
            return SettingValues.Format(_entry.BoxedValue, FormatToml);
        }

        private string FormatToml(object value)
        {
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
