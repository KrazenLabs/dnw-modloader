using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using BepInEx.Logging;

namespace BepInEx.Configuration
{
    public abstract class ConfigEntryBase
    {
        public ConfigFile ConfigFile { get; }
        public ConfigDefinition Definition { get; }
        public ConfigDescription Description { get; }
        public Type SettingType { get; }
        public object DefaultValue { get; }

        public abstract object BoxedValue { get; set; }

        internal ConfigEntryBase(ConfigFile configFile, ConfigDefinition definition, Type settingType, object defaultValue, ConfigDescription configDescription)
        {
            ConfigFile = configFile ?? throw new ArgumentNullException(nameof(configFile));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            SettingType = settingType ?? throw new ArgumentNullException(nameof(settingType));
            Description = configDescription ?? ConfigDescription.Empty;
            if (Description.AcceptableValues != null && !SettingType.IsAssignableFrom(Description.AcceptableValues.ValueType))
                throw new ArgumentException("configDescription.AcceptableValues is for a different type than the type of this setting");
            DefaultValue = defaultValue;
        }

        public string GetSerializedValue()
        {
            return TomlTypeConverter.ConvertToString(BoxedValue, SettingType);
        }

        public void SetSerializedValue(string value)
        {
            try
            {
                BoxedValue = TomlTypeConverter.ConvertToValue(value, SettingType);
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Warning, "Config value of setting \"" + Definition + "\" could not be parsed and will be ignored. Reason: " + e.Message + "; Value: " + value);
            }
        }

        protected T ClampValue<T>(T value)
        {
            return Description.AcceptableValues != null ? (T)Description.AcceptableValues.Clamp(value) : value;
        }

        protected void OnSettingChanged(object sender)
        {
            ConfigFile.OnSettingChanged(sender, this);
        }

        public void WriteDescription(StreamWriter writer)
        {
            if (!string.IsNullOrEmpty(Description.Description))
                writer.WriteLine("## " + Description.Description.Replace("\n", "\n## "));
            writer.WriteLine("# Setting type: " + SettingType.Name);
            writer.WriteLine("# Default value: " + TomlTypeConverter.ConvertToString(DefaultValue, SettingType));
            if (Description.AcceptableValues != null)
            {
                writer.WriteLine(Description.AcceptableValues.ToDescriptionString());
            }
            else if (SettingType.IsEnum)
            {
                writer.WriteLine("# Acceptable values: " + string.Join(", ", Enum.GetNames(SettingType)));
                if (SettingType.IsDefined(typeof(FlagsAttribute), true))
                    writer.WriteLine("# Multiple values can be set at the same time by separating them with , (e.g. Debug, Warning)");
            }
        }
    }

    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        private T _value;

        public event EventHandler SettingChanged;

        internal ConfigEntry(ConfigFile configFile, ConfigDefinition definition, T defaultValue, ConfigDescription configDescription)
            : base(configFile, definition, typeof(T), defaultValue, configDescription)
        {
            configFile.SettingChanged += (sender, args) =>
            {
                if (args.ChangedSetting == this) SettingChanged?.Invoke(sender, args);
            };
            Value = defaultValue;
        }

        public T Value
        {
            get { return _value; }
            set
            {
                value = ClampValue(value);
                if (Equals(_value, value)) return;
                _value = value;
                OnSettingChanged(this);
            }
        }

        public override object BoxedValue
        {
            get { return Value; }
            set { Value = (T)value; }
        }
    }

    [Obsolete("Use ConfigFile from new Bind overloads instead")]
    public sealed class ConfigWrapper<T>
    {
        public ConfigEntry<T> ConfigEntry { get; }
        public ConfigDefinition Definition { get { return ConfigEntry.Definition; } }
        public ConfigFile ConfigFile { get { return ConfigEntry.ConfigFile; } }

        public T Value
        {
            get { return ConfigEntry.Value; }
            set { ConfigEntry.Value = value; }
        }

        public event EventHandler SettingChanged;

        internal ConfigWrapper(ConfigEntry<T> configEntry)
        {
            ConfigEntry = configEntry ?? throw new ArgumentNullException(nameof(configEntry));
            configEntry.ConfigFile.SettingChanged += (sender, args) =>
            {
                if (args.ChangedSetting == configEntry) SettingChanged?.Invoke(sender, args);
            };
        }
    }

    // BepInEx's .cfg format
    public class ConfigFile : IDictionary<ConfigDefinition, ConfigEntryBase>
    {
        private readonly BepInPlugin _ownerMetadata;
        private readonly object _ioLock = new object();
        private readonly Dictionary<ConfigDefinition, string> _orphaned = new Dictionary<ConfigDefinition, string>();
        private bool _saveFailed;

        protected Dictionary<ConfigDefinition, ConfigEntryBase> Entries { get; } = new Dictionary<ConfigDefinition, ConfigEntryBase>();

        [Obsolete("Use Keys instead")]
        public ReadOnlyCollection<ConfigDefinition> ConfigDefinitions
        {
            get { lock (_ioLock) return Entries.Keys.ToList().AsReadOnly(); }
        }

        public string ConfigFilePath { get; }

        public bool SaveOnConfigSet { get; set; } = true;

        public event EventHandler ConfigReloaded;

        public event EventHandler<SettingChangedEventArgs> SettingChanged;

        public ConfigFile(string configPath, bool saveOnInit) : this(configPath, saveOnInit, null)
        {
        }

        public ConfigFile(string configPath, bool saveOnInit, BepInPlugin ownerMetadata)
        {
            if (configPath == null) throw new ArgumentNullException(nameof(configPath));
            _ownerMetadata = ownerMetadata;
            ConfigFilePath = Path.GetFullPath(configPath);
            if (File.Exists(ConfigFilePath)) Reload();
            else if (saveOnInit) Save();
        }

        [Obsolete("Use Values instead")]
        public ConfigEntryBase[] GetConfigEntries()
        {
            lock (_ioLock) return Entries.Values.ToArray();
        }

        public void Reload()
        {
            lock (_ioLock)
            {
                _orphaned.Clear();
                string section = string.Empty;
                foreach (var rawLine in File.ReadAllLines(ConfigFilePath))
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2);
                        continue;
                    }
                    var pair = line.Split(new[] { '=' }, 2);
                    if (pair.Length != 2) continue;

                    var definition = new ConfigDefinition(section, pair[0].Trim());
                    string value = pair[1].Trim();
                    if (Entries.TryGetValue(definition, out var entry)) entry.SetSerializedValue(value);
                    else _orphaned[definition] = value;
                }
            }
            RaiseEach(ConfigReloaded, handler => handler(this, EventArgs.Empty));
        }

        public void Save()
        {
            lock (_ioLock)
            {
                try
                {
                    Write();
                    _saveFailed = false;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    if (_saveFailed) return;
                    _saveFailed = true;
                    Logger.Log(LogLevel.Warning, "Could not save " + ConfigFilePath + ": " + e.Message);
                }
            }
        }

        private void Write()
        {
            string directory = Path.GetDirectoryName(ConfigFilePath);
            if (directory != null) Directory.CreateDirectory(directory);

            var lines = Entries.Select(x => new { x.Key, Entry = x.Value, Value = x.Value.GetSerializedValue() })
                .Concat(_orphaned.Select(x => new { x.Key, Entry = (ConfigEntryBase)null, x.Value }));

            using (var writer = new StreamWriter(ConfigFilePath, false, Utility.UTF8NoBom))
            {
                if (_ownerMetadata != null)
                {
                    writer.WriteLine("Settings file created by plugin " + _ownerMetadata.Name + " v" + _ownerMetadata.Version);
                    writer.WriteLine("Plugin GUID: " + _ownerMetadata.GUID);
                    writer.WriteLine();
                }
                foreach (var section in lines.GroupBy(x => x.Key.Section).OrderBy(x => x.Key))
                {
                    writer.WriteLine("[" + section.Key + "]");
                    foreach (var line in section)
                    {
                        writer.WriteLine();
                        line.Entry?.WriteDescription(writer);
                        writer.WriteLine(line.Key.Key + " = " + line.Value);
                    }
                    writer.WriteLine();
                }
            }
        }

        [Obsolete("Use ConfigFile[key] or TryGetEntry instead")]
        public ConfigEntry<T> GetSetting<T>(ConfigDefinition configDefinition)
        {
            return TryGetEntry(configDefinition, out ConfigEntry<T> entry) ? entry : null;
        }

        [Obsolete("Use ConfigFile[key] or TryGetEntry instead")]
        public ConfigEntry<T> GetSetting<T>(string section, string key)
        {
            return TryGetEntry(section, key, out ConfigEntry<T> entry) ? entry : null;
        }

        public bool TryGetEntry<T>(ConfigDefinition configDefinition, out ConfigEntry<T> entry)
        {
            lock (_ioLock)
            {
                if (Entries.TryGetValue(configDefinition, out var found))
                {
                    entry = (ConfigEntry<T>)found;
                    return true;
                }
            }
            entry = null;
            return false;
        }

        public bool TryGetEntry<T>(string section, string key, out ConfigEntry<T> entry)
        {
            return TryGetEntry(new ConfigDefinition(section, key), out entry);
        }

        public ConfigEntry<T> Bind<T>(ConfigDefinition configDefinition, T defaultValue, ConfigDescription configDescription = null)
        {
            if (!TomlTypeConverter.CanConvert(typeof(T)))
                throw new ArgumentException("Type " + typeof(T) + " is not supported by the config system. Supported types: " + string.Join(", ", TomlTypeConverter.GetSupportedTypes().Select(x => x.Name).ToArray()));

            lock (_ioLock)
            {
                if (Entries.TryGetValue(configDefinition, out var existing)) return (ConfigEntry<T>)existing;

                var entry = new ConfigEntry<T>(this, configDefinition, defaultValue, configDescription);
                Entries[configDefinition] = entry;
                if (_orphaned.TryGetValue(configDefinition, out string stored))
                {
                    entry.SetSerializedValue(stored);
                    _orphaned.Remove(configDefinition);
                }
                if (SaveOnConfigSet) Save();
                return entry;
            }
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription configDescription = null)
        {
            return Bind(new ConfigDefinition(section, key), defaultValue, configDescription);
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Bind(new ConfigDefinition(section, key), defaultValue, new ConfigDescription(description));
        }

        [Obsolete("Use Bind instead")]
        public ConfigEntry<T> AddSetting<T>(ConfigDefinition configDefinition, T defaultValue, ConfigDescription configDescription = null)
        {
            return Bind(configDefinition, defaultValue, configDescription);
        }

        [Obsolete("Use Bind instead")]
        public ConfigEntry<T> AddSetting<T>(string section, string key, T defaultValue, ConfigDescription configDescription = null)
        {
            return Bind(new ConfigDefinition(section, key), defaultValue, configDescription);
        }

        [Obsolete("Use Bind instead")]
        public ConfigEntry<T> AddSetting<T>(string section, string key, T defaultValue, string description)
        {
            return Bind(new ConfigDefinition(section, key), defaultValue, new ConfigDescription(description));
        }

        [Obsolete("Use Bind instead")]
        public ConfigWrapper<T> Wrap<T>(string section, string key, string description = null, T defaultValue = default(T))
        {
            var definition = new ConfigDefinition(section, key, description);
            return new ConfigWrapper<T>(Bind(definition, defaultValue, string.IsNullOrEmpty(description) ? null : new ConfigDescription(description)));
        }

        [Obsolete("Use Bind instead")]
        public ConfigWrapper<T> Wrap<T>(ConfigDefinition configDefinition, T defaultValue = default(T))
        {
            return Wrap(configDefinition.Section, configDefinition.Key, null, defaultValue);
        }

        internal void OnSettingChanged(object sender, ConfigEntryBase changedEntry)
        {
            if (changedEntry == null) throw new ArgumentNullException(nameof(changedEntry));
            if (SaveOnConfigSet) Save();
            var args = new SettingChangedEventArgs(changedEntry);
            RaiseEach(SettingChanged, handler => handler(sender, args));
        }

        // Every handler runs even when an earlier one throws
        private static void RaiseEach<THandler>(THandler handlers, Action<THandler> invoke) where THandler : class
        {
            var multicast = handlers as Delegate;
            if (multicast == null) return;
            foreach (var handler in multicast.GetInvocationList())
            {
                try { invoke((THandler)(object)handler); }
                catch (Exception e) { Logger.Log(LogLevel.Error, e); }
            }
        }

        public ConfigEntryBase this[ConfigDefinition key]
        {
            get { lock (_ioLock) return Entries[key]; }
        }

        ConfigEntryBase IDictionary<ConfigDefinition, ConfigEntryBase>.this[ConfigDefinition key]
        {
            get { lock (_ioLock) return Entries[key]; }
            set { throw new InvalidOperationException("Directly setting a config entry is not supported"); }
        }

        public ConfigEntryBase this[string section, string key]
        {
            get { return this[new ConfigDefinition(section, key)]; }
        }

        public ICollection<ConfigDefinition> Keys
        {
            get { lock (_ioLock) return Entries.Keys.ToArray(); }
        }

        ICollection<ConfigEntryBase> IDictionary<ConfigDefinition, ConfigEntryBase>.Values
        {
            get { lock (_ioLock) return Entries.Values.ToArray(); }
        }

        public int Count
        {
            get { lock (_ioLock) return Entries.Count; }
        }

        public bool IsReadOnly { get { return false; } }

        public IEnumerator<KeyValuePair<ConfigDefinition, ConfigEntryBase>> GetEnumerator()
        {
            return Entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool Contains(KeyValuePair<ConfigDefinition, ConfigEntryBase> item)
        {
            lock (_ioLock) return ((ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>)Entries).Contains(item);
        }

        public bool ContainsKey(ConfigDefinition key)
        {
            lock (_ioLock) return Entries.ContainsKey(key);
        }

        public void Add(ConfigDefinition key, ConfigEntryBase value)
        {
            throw new InvalidOperationException("Directly adding a config entry is not supported");
        }

        void ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>.Add(KeyValuePair<ConfigDefinition, ConfigEntryBase> item)
        {
            lock (_ioLock) Entries.Add(item.Key, item.Value);
        }

        void ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>.CopyTo(KeyValuePair<ConfigDefinition, ConfigEntryBase>[] array, int arrayIndex)
        {
            lock (_ioLock) ((ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>)Entries).CopyTo(array, arrayIndex);
        }

        bool ICollection<KeyValuePair<ConfigDefinition, ConfigEntryBase>>.Remove(KeyValuePair<ConfigDefinition, ConfigEntryBase> item)
        {
            lock (_ioLock) return Entries.Remove(item.Key);
        }

        public bool Remove(ConfigDefinition key)
        {
            lock (_ioLock) return Entries.Remove(key);
        }

        public void Clear()
        {
            lock (_ioLock) Entries.Clear();
        }

        bool IDictionary<ConfigDefinition, ConfigEntryBase>.TryGetValue(ConfigDefinition key, out ConfigEntryBase value)
        {
            lock (_ioLock) return Entries.TryGetValue(key, out value);
        }
    }
}
