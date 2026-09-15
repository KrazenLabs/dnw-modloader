using System;
using System.Linq;

namespace BepInEx.Configuration
{
    public class ConfigDefinition : IEquatable<ConfigDefinition>
    {
        private static readonly char[] InvalidChars = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };

        public string Section { get; }
        public string Key { get; }

        public ConfigDefinition(string section, string key)
        {
            Validate(section, nameof(section));
            Validate(key, nameof(key));
            Section = section;
            Key = key;
        }

        [Obsolete("description argument is no longer used, put it in a ConfigDescription instead")]
        public ConfigDefinition(string section, string key, string description)
        {
            Section = section ?? "";
            Key = key ?? "";
        }

        private static void Validate(string value, string name)
        {
            if (value == null) throw new ArgumentNullException(name);
            if (value != value.Trim()) throw new ArgumentException("Cannot use whitespace characters at start or end of section and key names", name);
            if (value.IndexOfAny(InvalidChars) >= 0) throw new ArgumentException("Cannot use any of the following characters in section and key names: = \\n \\t \\ \" ' [ ]", name);
        }

        public bool Equals(ConfigDefinition other)
        {
            return !ReferenceEquals(other, null) && string.Equals(Key, other.Key) && string.Equals(Section, other.Section);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ConfigDefinition);
        }

        public override int GetHashCode()
        {
            unchecked { return ((Key != null ? Key.GetHashCode() : 0) * 397) ^ (Section != null ? Section.GetHashCode() : 0); }
        }

        public static bool operator ==(ConfigDefinition left, ConfigDefinition right) { return Equals(left, right); }

        public static bool operator !=(ConfigDefinition left, ConfigDefinition right) { return !Equals(left, right); }

        public override string ToString()
        {
            return Section + "." + Key;
        }
    }

    public class ConfigDescription
    {
        public string Description { get; }
        public AcceptableValueBase AcceptableValues { get; }
        public object[] Tags { get; }

        public static ConfigDescription Empty { get; } = new ConfigDescription("");

        public ConfigDescription(string description, AcceptableValueBase acceptableValues = null, params object[] tags)
        {
            Description = description ?? throw new ArgumentNullException(nameof(description));
            AcceptableValues = acceptableValues;
            Tags = tags;
        }
    }

    public abstract class AcceptableValueBase
    {
        public Type ValueType { get; }

        protected AcceptableValueBase(Type valueType)
        {
            ValueType = valueType;
        }

        public abstract object Clamp(object value);
        public abstract bool IsValid(object value);
        public abstract string ToDescriptionString();
    }

    public class AcceptableValueRange<T> : AcceptableValueBase where T : IComparable
    {
        public virtual T MinValue { get; }
        public virtual T MaxValue { get; }

        public AcceptableValueRange(T minValue, T maxValue) : base(typeof(T))
        {
            if (maxValue == null) throw new ArgumentNullException(nameof(maxValue));
            if (minValue == null) throw new ArgumentNullException(nameof(minValue));
            if (minValue.CompareTo(maxValue) >= 0) throw new ArgumentException("minValue has to be lower than maxValue");
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public override object Clamp(object value)
        {
            if (MinValue.CompareTo(value) > 0) return MinValue;
            if (MaxValue.CompareTo(value) < 0) return MaxValue;
            return value;
        }

        public override bool IsValid(object value)
        {
            return MinValue.CompareTo(value) <= 0 && MaxValue.CompareTo(value) >= 0;
        }

        public override string ToDescriptionString()
        {
            return "# Acceptable value range: From " + MinValue + " to " + MaxValue;
        }
    }

    public class AcceptableValueList<T> : AcceptableValueBase where T : IEquatable<T>
    {
        public virtual T[] AcceptableValues { get; }

        public AcceptableValueList(params T[] acceptableValues) : base(typeof(T))
        {
            if (acceptableValues == null) throw new ArgumentNullException(nameof(acceptableValues));
            if (acceptableValues.Length == 0) throw new ArgumentException("At least one acceptable value is needed", nameof(acceptableValues));
            AcceptableValues = acceptableValues;
        }

        public override object Clamp(object value)
        {
            return IsValid(value) ? value : AcceptableValues[0];
        }

        public override bool IsValid(object value)
        {
            return value is T typed && AcceptableValues.Any(x => x.Equals(typed));
        }

        public override string ToDescriptionString()
        {
            return "# Acceptable values: " + string.Join(", ", AcceptableValues.Select(x => x.ToString()).ToArray());
        }
    }

    public sealed class SettingChangedEventArgs : EventArgs
    {
        public ConfigEntryBase ChangedSetting { get; }

        public SettingChangedEventArgs(ConfigEntryBase changedSetting)
        {
            ChangedSetting = changedSetting;
        }
    }

    public class TypeConverter
    {
        public Func<object, Type, string> ConvertToString { get; set; }
        public Func<string, Type, object> ConvertToObject { get; set; }
    }
}
