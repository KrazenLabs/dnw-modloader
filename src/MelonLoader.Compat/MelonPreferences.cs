using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader.Preferences;
using Tomlet;
using Tomlet.Models;

namespace MelonLoader.Preferences
{
    public abstract class ValueValidator
    {
        public abstract bool IsValid(object value);
        public abstract object EnsureValid(object value);
    }

    public interface IValueRange
    {
        object MinValue { get; }
        object MaxValue { get; }
    }

    public class ValueRange<T> : ValueValidator, IValueRange where T : IComparable<T>
    {
        public ValueRange(T min, T max)
        {
            Min = min;
            Max = max;
        }

        public T Min { get; private set; }
        public T Max { get; private set; }

        public object MinValue { get { return Min; } }
        public object MaxValue { get { return Max; } }

        public override bool IsValid(object value)
        {
            if (!(value is T)) return false;
            var typed = (T)value;
            return typed.CompareTo(Min) >= 0 && typed.CompareTo(Max) <= 0;
        }

        public override object EnsureValid(object value)
        {
            if (!(value is T)) return Min;
            var typed = (T)value;
            if (typed.CompareTo(Min) < 0) return Min;
            if (typed.CompareTo(Max) > 0) return Max;
            return typed;
        }
    }
}

namespace MelonLoader
{
    public abstract class MelonPreferences_Entry
    {
        protected MelonPreferences_Entry() { }

        public string Identifier { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Description { get; internal set; }
        public string Comment { get { return Description; } }
        public MelonPreferences_Category Category { get; internal set; }
        public bool IsHidden { get; internal set; }
        public bool DontSaveDefault { get; internal set; }
        public ValueValidator Validator { get; internal set; }

        public virtual object BoxedValue { get; set; }
        public virtual object BoxedEditedValue { get; set; }

        public event Action OnValueChangedUntyped;

        protected void FireUntypedValueChanged(object old, object neew)
        {
            var handler = OnValueChangedUntyped;
            if (handler == null) return;
            try { handler(); }
            catch (Exception e) { MelonLogger.Error("OnValueChangedUntyped threw for " + Identifier + ": " + e.Message); }
        }

        public string GetExceptionMessage(string submsg)
        {
            return "Preference '" + Identifier + "' of category '" + (Category != null ? Category.Identifier : "?") + "' " + submsg;
        }

        public virtual Type GetReflectedType() { return typeof(object); }
        public virtual string GetValueAsString() { return BoxedValue != null ? BoxedValue.ToString() : ""; }
        public virtual string GetEditedValueAsString() { return BoxedEditedValue != null ? BoxedEditedValue.ToString() : ""; }
        public virtual string GetDefaultValueAsString() { return ""; }

        public abstract void Load(TomlValue obj);
        public abstract TomlValue Save();
        public abstract void ResetToDefault();
    }

    public class MelonPreferences_Entry<T> : MelonPreferences_Entry
    {
        private T _value;
        private T _editedValue;

        public MelonPreferences_Entry() { }

        public T Value
        {
            get { return _value; }
            set
            {
                var validator = Validator;
                if (validator != null) value = (T)validator.EnsureValid(value);
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                T old = _value;
                _value = value;
                _editedValue = value;
                FireValueChanged(old, value);
            }
        }

        public T EditedValue
        {
            get { return _editedValue; }
            set { _editedValue = value; }
        }

        public T DefaultValue { get; set; }

        public override object BoxedValue
        {
            get { return _value; }
            set { if (value is T) Value = (T)value; }
        }

        public override object BoxedEditedValue
        {
            get { return _editedValue; }
            set { if (value is T) _editedValue = (T)value; }
        }

        public event Action<T, T> OnValueChanged;

        private void FireValueChanged(T old, T neew)
        {
            var handler = OnValueChanged;
            if (handler != null)
            {
                try { handler(old, neew); }
                catch (Exception e) { MelonLogger.Error("OnValueChanged threw for " + Identifier + ": " + e.Message); }
            }
            FireUntypedValueChanged(old, neew);
        }

        public override Type GetReflectedType() { return typeof(T); }
        public override string GetValueAsString() { return _value != null ? _value.ToString() : ""; }
        public override string GetEditedValueAsString() { return _editedValue != null ? _editedValue.ToString() : ""; }
        public override string GetDefaultValueAsString() { return DefaultValue != null ? DefaultValue.ToString() : ""; }

        public override void ResetToDefault() { Value = DefaultValue; }

        public override void Load(TomlValue obj)
        {
            if (obj == null) return;
            try { Value = TomletMain.To<T>(obj); }
            catch (Exception e) { MelonLogger.Warning(GetExceptionMessage("could not be read: " + e.Message)); }
        }

        public override TomlValue Save()
        {
            try
            {
                _editedValue = _value;
                return TomletMain.ValueFrom(_value);
            }
            catch (Exception e)
            {
                MelonLogger.Warning(GetExceptionMessage("could not be written: " + e.Message));
                return null;
            }
        }
    }

    public class MelonPreferences_Category
    {
        public readonly List<MelonPreferences_Entry> Entries = new List<MelonPreferences_Entry>();

        internal MelonPreferences_Category(string identifier, string displayName, bool isHidden = false)
        {
            Identifier = identifier;
            DisplayName = string.IsNullOrEmpty(displayName) ? identifier : displayName;
            IsHidden = isHidden;
        }

        public string Identifier { get; private set; }
        public string DisplayName { get; set; }
        public bool IsHidden { get; set; }
        public bool IsInlined { get; set; }

        public string FilePath { get; private set; }

        public MelonPreferences_Entry<T> CreateEntry<T>(string identifier, T default_value, string display_name = null, bool is_hidden = false)
        {
            return CreateEntry(identifier, default_value, display_name, null, is_hidden, false, null);
        }

        public MelonPreferences_Entry<T> CreateEntry<T>(string identifier, T default_value, string display_name, string description,
            bool is_hidden = false, bool dont_save_default = false, ValueValidator validator = null, string oldIdentifier = null)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentNullException("identifier");

            var existing = GetEntry<T>(identifier);
            if (existing != null) return existing;

            if (validator != null && !validator.IsValid(default_value))
                default_value = (T)validator.EnsureValid(default_value);

            var entry = new MelonPreferences_Entry<T>
            {
                Identifier = identifier,
                DisplayName = string.IsNullOrEmpty(display_name) ? identifier : display_name,
                Description = description,
                Category = this,
                IsHidden = is_hidden,
                DontSaveDefault = dont_save_default,
                Validator = validator,
                DefaultValue = default_value,
            };
            entry.Value = default_value;
            entry.EditedValue = default_value;

            lock (Entries) Entries.Add(entry);
            MelonPreferences.ApplyStoredValue(this, entry, oldIdentifier);
            return entry;
        }

        public MelonPreferences_Entry GetEntry(string identifier)
        {
            lock (Entries) return Entries.FirstOrDefault(e => string.Equals(e.Identifier, identifier, StringComparison.Ordinal));
        }

        public MelonPreferences_Entry<T> GetEntry<T>(string identifier)
        {
            return GetEntry(identifier) as MelonPreferences_Entry<T>;
        }

        public bool HasEntry(string identifier) { return GetEntry(identifier) != null; }

        public void DeleteEntry(string identifier)
        {
            var entry = GetEntry(identifier);
            if (entry == null) return;
            lock (Entries) Entries.Remove(entry);
        }

        public void RenameEntry(string identifier, string newIdentifier)
        {
            var entry = GetEntry(identifier);
            if (entry == null || string.IsNullOrEmpty(newIdentifier)) return;
            entry.Identifier = newIdentifier;
        }

        public void SetFilePath(string filepath) { SetFilePath(filepath, true, true); }
        public void SetFilePath(string filepath, bool autoload) { SetFilePath(filepath, autoload, true); }

        public void SetFilePath(string filepath, bool autoload, bool printmsg)
        {
            FilePath = filepath;
            if (autoload) LoadFromFile(printmsg);
        }

        public void ResetFilePath()
        {
            FilePath = null;
            LoadFromFile(false);
        }

        public void SaveToFile(bool printmsg = true) { MelonPreferences.SaveCategoryInternal(this, printmsg); }
        public void LoadFromFile(bool printmsg = true) { MelonPreferences.LoadCategoryInternal(this, printmsg); }

        public void DestroyFileWatcher() { }
    }

    public static class MelonPreferences
    {
        public static readonly List<MelonPreferences_Category> Categories = new List<MelonPreferences_Category>();

        internal static string DefaultFilePath;
        internal static Action<MelonPreferences_Category, string> Saved;
        internal static Action<MelonPreferences_Category, string> Loaded;

        private static readonly Dictionary<string, TomlDocument> Documents = new Dictionary<string, TomlDocument>(StringComparer.OrdinalIgnoreCase);

        public static MelonPreferences_Category CreateCategory(string identifier) { return CreateCategory(identifier, null, false, false); }
        public static MelonPreferences_Category CreateCategory(string identifier, string display_name) { return CreateCategory(identifier, display_name, false, false); }

        public static MelonPreferences_Category CreateCategory(string identifier, string display_name, bool is_hidden, bool should_save)
        {
            if (string.IsNullOrEmpty(identifier)) throw new ArgumentNullException("identifier");
            var existing = GetCategory(identifier);
            if (existing != null) return existing;

            var category = new MelonPreferences_Category(identifier, display_name, is_hidden);
            lock (Categories) Categories.Add(category);
            return category;
        }

        public static MelonPreferences_Category GetCategory(string identifier)
        {
            lock (Categories) return Categories.FirstOrDefault(c => string.Equals(c.Identifier, identifier, StringComparison.Ordinal));
        }

        public static MelonPreferences_Entry<T> CreateEntry<T>(string category_identifier, string entry_identifier, T default_value,
            string display_name = null, bool is_hidden = false)
        {
            return CreateEntry<T>(category_identifier, entry_identifier, default_value, display_name, null, is_hidden, false, null);
        }

        public static MelonPreferences_Entry<T> CreateEntry<T>(string category_identifier, string entry_identifier, T default_value,
            string display_name, string description, bool is_hidden = false, bool dont_save_default = false, ValueValidator validator = null)
        {
            var category = GetCategory(category_identifier) ?? CreateCategory(category_identifier);
            return category.CreateEntry(entry_identifier, default_value, display_name, description, is_hidden, dont_save_default, validator);
        }

        public static MelonPreferences_Entry GetEntry(string category_identifier, string entry_identifier)
        {
            var category = GetCategory(category_identifier);
            return category != null ? category.GetEntry(entry_identifier) : null;
        }

        public static MelonPreferences_Entry<T> GetEntry<T>(string category_identifier, string entry_identifier)
        {
            return GetEntry(category_identifier, entry_identifier) as MelonPreferences_Entry<T>;
        }

        public static bool HasEntry(string category_identifier, string entry_identifier) { return GetEntry(category_identifier, entry_identifier) != null; }

        public static T GetEntryValue<T>(string category_identifier, string entry_identifier)
        {
            var entry = GetEntry<T>(category_identifier, entry_identifier);
            return entry != null ? entry.Value : default(T);
        }

        public static void SetEntryValue<T>(string category_identifier, string entry_identifier, T value)
        {
            var entry = GetEntry<T>(category_identifier, entry_identifier);
            if (entry != null) entry.Value = value;
        }

        public static void Load()
        {
            MelonPreferences_Category[] all;
            lock (Categories) all = Categories.ToArray();
            foreach (var category in all) LoadCategoryInternal(category, false);
        }

        public static void Save()
        {
            MelonPreferences_Category[] all;
            lock (Categories) all = Categories.ToArray();
            foreach (var category in all) SaveCategoryInternal(category, false);
        }

        public static void SaveCategory<T>(string category_identifier, bool printmsg = true)
        {
            var category = GetCategory(category_identifier);
            if (category != null) SaveCategoryInternal(category, printmsg);
        }

        public static void RemoveCategoryFromFile(string filepath, string category_identifier)
        {
            Documents.Remove(ResolvePath(filepath));
            Save();
        }

        private static string ResolvePath(string path) { return string.IsNullOrEmpty(path) ? DefaultFilePath : Path.GetFullPath(path); }

        private static string PathFor(MelonPreferences_Category category)
        {
            return !string.IsNullOrEmpty(category.FilePath) ? Path.GetFullPath(category.FilePath) : DefaultFilePath;
        }

        private static TomlDocument DocumentFor(string path, bool reread)
        {
            if (string.IsNullOrEmpty(path)) return TomlDocument.CreateEmpty();

            TomlDocument document;
            if (!reread && Documents.TryGetValue(path, out document)) return document;

            try
            {
                document = File.Exists(path) ? new TomlParser().Parse(File.ReadAllText(path)) : TomlDocument.CreateEmpty();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not read " + path + ": " + e.Message);
                document = TomlDocument.CreateEmpty();
            }

            Documents[path] = document;
            return document;
        }

        internal static void ApplyStoredValue(MelonPreferences_Category category, MelonPreferences_Entry entry, string oldIdentifier)
        {
            string path = PathFor(category);
            if (string.IsNullOrEmpty(path)) return;

            var document = DocumentFor(path, false);
            if (!document.ContainsKey(category.Identifier)) return;

            try
            {
                var table = document.GetSubTable(category.Identifier);
                if (table == null) return;

                TomlValue value;
                if (!table.TryGetValue(entry.Identifier, out value) && !string.IsNullOrEmpty(oldIdentifier))
                    table.TryGetValue(oldIdentifier, out value);
                if (value != null) entry.Load(value);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not apply the stored value of " + category.Identifier + "." + entry.Identifier + ": " + e.Message);
            }
        }

        internal static void LoadCategoryInternal(MelonPreferences_Category category, bool printmsg)
        {
            string path = PathFor(category);
            if (string.IsNullOrEmpty(path)) return;

            var document = DocumentFor(path, true);
            if (document.ContainsKey(category.Identifier))
            {
                try
                {
                    var table = document.GetSubTable(category.Identifier);
                    MelonPreferences_Entry[] entries;
                    lock (category.Entries) entries = category.Entries.ToArray();
                    foreach (var entry in entries)
                    {
                        TomlValue value;
                        if (table != null && table.TryGetValue(entry.Identifier, out value)) entry.Load(value);
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("Could not load category " + category.Identifier + ": " + e.Message);
                }
            }

            if (printmsg) MelonLogger.Msg("Loaded preferences for " + category.DisplayName + ".");
            var loaded = Loaded;
            if (loaded != null) loaded(category, path);
        }

        internal static void SaveCategoryInternal(MelonPreferences_Category category, bool printmsg)
        {
            string path = PathFor(category);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var document = DocumentFor(path, false);
                var table = document.ContainsKey(category.Identifier) ? document.GetSubTable(category.Identifier) : new TomlTable();

                MelonPreferences_Entry[] entries;
                lock (category.Entries) entries = category.Entries.ToArray();
                foreach (var entry in entries)
                {
                    if (entry.DontSaveDefault && entry.GetValueAsString() == entry.GetDefaultValueAsString()) continue;
                    var value = entry.Save();
                    if (value == null) continue;
                    if (!string.IsNullOrEmpty(entry.Description)) value.Comments.PrecedingComment = entry.Description;
                    table.PutValue(entry.Identifier, value, true);
                }

                if (!document.ContainsKey(category.Identifier)) document.PutValue(category.Identifier, table, true);

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, document.SerializedValue);
            }
            catch (Exception e)
            {
                MelonLogger.Error("Could not save category " + category.Identifier + " to " + path + ": " + e.Message);
                return;
            }

            if (printmsg) MelonLogger.Msg("Saved preferences for " + category.DisplayName + ".");
            var saved = Saved;
            if (saved != null) saved(category, path);
        }
    }

    // Pre-0.3 preference API
    public class MelonPrefs
    {
        private const string LegacyCategory = "ModPrefs";

        public static void RegisterCategory(string name, string displayText) { MelonPreferences.CreateCategory(name, displayText); }
        public static void RegisterString(string section, string name, string defaultValue, string displayText = null, bool hideFromList = false) { MelonPreferences.CreateEntry(section ?? LegacyCategory, name, defaultValue, displayText, hideFromList); }
        public static void RegisterBool(string section, string name, bool defaultValue, string displayText = null, bool hideFromList = false) { MelonPreferences.CreateEntry(section ?? LegacyCategory, name, defaultValue, displayText, hideFromList); }
        public static void RegisterInt(string section, string name, int defaultValue, string displayText = null, bool hideFromList = false) { MelonPreferences.CreateEntry(section ?? LegacyCategory, name, defaultValue, displayText, hideFromList); }
        public static void RegisterFloat(string section, string name, float defaultValue, string displayText = null, bool hideFromList = false) { MelonPreferences.CreateEntry(section ?? LegacyCategory, name, defaultValue, displayText, hideFromList); }

        public static string GetString(string section, string name) { return MelonPreferences.GetEntryValue<string>(section ?? LegacyCategory, name); }
        public static bool GetBool(string section, string name) { return MelonPreferences.GetEntryValue<bool>(section ?? LegacyCategory, name); }
        public static int GetInt(string section, string name) { return MelonPreferences.GetEntryValue<int>(section ?? LegacyCategory, name); }
        public static float GetFloat(string section, string name) { return MelonPreferences.GetEntryValue<float>(section ?? LegacyCategory, name); }

        public static void SetString(string section, string name, string value) { MelonPreferences.SetEntryValue(section ?? LegacyCategory, name, value); }
        public static void SetBool(string section, string name, bool value) { MelonPreferences.SetEntryValue(section ?? LegacyCategory, name, value); }
        public static void SetInt(string section, string name, int value) { MelonPreferences.SetEntryValue(section ?? LegacyCategory, name, value); }
        public static void SetFloat(string section, string name, float value) { MelonPreferences.SetEntryValue(section ?? LegacyCategory, name, value); }

        public static void SaveConfig() { MelonPreferences.Save(); }
    }

    public class ModPrefs : MelonPrefs { }
}
