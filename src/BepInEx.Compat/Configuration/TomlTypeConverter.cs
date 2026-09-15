using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using UnityEngine;

namespace BepInEx.Configuration
{
    public static class TomlTypeConverter
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<Type, TypeConverter> Converters = CreateDefaultConverters();
        private static readonly TypeConverter EnumConverter = Converter((o, t) => o.ToString(), (s, t) => Enum.Parse(t, s, true));
        private static readonly Regex WindowsPath = new Regex("^\"?\\w:\\\\(?!\\\\)(?!.+\\\\\\\\)");
        private static bool _unityConvertersAdded;

        public static string ConvertToString(object value, Type valueType)
        {
            var converter = GetConverter(valueType) ?? throw new InvalidOperationException("Cannot convert from type " + valueType);
            return converter.ConvertToString(value, valueType);
        }

        public static T ConvertToValue<T>(string value)
        {
            return (T)ConvertToValue(value, typeof(T));
        }

        public static object ConvertToValue(string value, Type valueType)
        {
            var converter = GetConverter(valueType) ?? throw new InvalidOperationException("Cannot convert to type " + valueType.Name);
            return converter.ConvertToObject(value, valueType);
        }

        public static TypeConverter GetConverter(Type valueType)
        {
            if (valueType == null) throw new ArgumentNullException(nameof(valueType));
            if (valueType.IsEnum) return EnumConverter;
            if (valueType == typeof(KeyboardShortcut)) RuntimeHelpers.RunClassConstructor(typeof(KeyboardShortcut).TypeHandle);
            lock (Sync)
            {
                if (Converters.TryGetValue(valueType, out var converter)) return converter;
                if (_unityConvertersAdded) return null;
                _unityConvertersAdded = true;
            }
            AddUnityConverters();
            lock (Sync) return Converters.TryGetValue(valueType, out var added) ? added : null;
        }

        public static bool AddConverter(Type type, TypeConverter converter)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            bool exists = CanConvert(type);
            lock (Sync)
            {
                if (exists || Converters.ContainsKey(type))
                {
                    Logging.Logger.LogWarning("Tried to add a TomlConverter when one already exists for type " + type.FullName);
                    return false;
                }
                Converters.Add(type, converter);
                return true;
            }
        }

        public static bool CanConvert(Type type)
        {
            return GetConverter(type) != null;
        }

        public static IEnumerable<Type> GetSupportedTypes()
        {
            lock (Sync) return new List<Type>(Converters.Keys) { typeof(Enum) };
        }

        private static TypeConverter Converter(Func<object, Type, string> toString, Func<string, Type, object> toObject)
        {
            return new TypeConverter { ConvertToString = toString, ConvertToObject = toObject };
        }

        private static Dictionary<Type, TypeConverter> CreateDefaultConverters()
        {
            var invariant = CultureInfo.InvariantCulture;
            return new Dictionary<Type, TypeConverter>
            {
                [typeof(string)] = Converter((o, t) => Escape((string)o), (s, t) => WindowsPath.IsMatch(s) ? s : Unescape(s)),
                [typeof(bool)] = Converter((o, t) => o.ToString().ToLowerInvariant(), (s, t) => bool.Parse(s)),
                [typeof(byte)] = Converter((o, t) => ((byte)o).ToString(invariant), (s, t) => byte.Parse(s, invariant)),
                [typeof(sbyte)] = Converter((o, t) => ((sbyte)o).ToString(invariant), (s, t) => sbyte.Parse(s, invariant)),
                [typeof(short)] = Converter((o, t) => ((short)o).ToString(invariant), (s, t) => short.Parse(s, invariant)),
                [typeof(ushort)] = Converter((o, t) => ((ushort)o).ToString(invariant), (s, t) => ushort.Parse(s, invariant)),
                [typeof(int)] = Converter((o, t) => ((int)o).ToString(invariant), (s, t) => int.Parse(s, invariant)),
                [typeof(uint)] = Converter((o, t) => ((uint)o).ToString(invariant), (s, t) => uint.Parse(s, invariant)),
                [typeof(long)] = Converter((o, t) => ((long)o).ToString(invariant), (s, t) => long.Parse(s, invariant)),
                [typeof(ulong)] = Converter((o, t) => ((ulong)o).ToString(invariant), (s, t) => ulong.Parse(s, invariant)),
                [typeof(float)] = Converter((o, t) => ((float)o).ToString(invariant), (s, t) => float.Parse(s, invariant)),
                [typeof(double)] = Converter((o, t) => ((double)o).ToString(invariant), (s, t) => double.Parse(s, invariant)),
                [typeof(decimal)] = Converter((o, t) => ((decimal)o).ToString(invariant), (s, t) => decimal.Parse(s, invariant)),
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddUnityConverters()
        {
            try
            {
                AddConverter(typeof(Color), Converter(
                    (o, t) => ColorUtility.ToHtmlStringRGBA((Color)o),
                    (s, t) =>
                    {
                        if (!ColorUtility.TryParseHtmlString("#" + s.Trim('#', ' '), out Color color))
                            throw new FormatException("Invalid color string, expected hex #RRGGBBAA");
                        return color;
                    }));
                var json = Converter((o, t) => JsonUtility.ToJson(o), (s, t) => JsonUtility.FromJson(s, t));
                AddConverter(typeof(Vector2), json);
                AddConverter(typeof(Vector3), json);
                AddConverter(typeof(Vector4), json);
                AddConverter(typeof(Quaternion), json);
                AddConverter(typeof(Rect), Converter(RectToString, StringToRect));
            }
            catch (Exception e)
            {
                Logging.Logger.LogWarning("Failed to load UnityEngine Toml converters - " + e.Message);
            }
        }

        private static string RectToString(object value, Type type)
        {
            var rect = (Rect)value;
            return string.Format(CultureInfo.InvariantCulture, "{{ \"x\":{0}, \"y\":{1}, \"width\":{2}, \"height\":{3} }}", rect.x, rect.y, rect.width, rect.height);
        }

        private static object StringToRect(string text, Type type)
        {
            var rect = new Rect();
            if (text == null) return rect;
            foreach (var part in text.Trim('{', '}').Replace(" ", "").Split(','))
            {
                var pair = part.Split(':');
                if (pair.Length != 2 || !float.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float number)) continue;
                switch (pair[0].Trim('"'))
                {
                    case "x": rect.x = number; break;
                    case "y": rect.y = number; break;
                    case "width":
                    case "z": rect.width = number; break;
                    case "height":
                    case "w": rect.height = number; break;
                }
            }
            return rect;
        }

        // Backslashes are read back as escapes
        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length + 2);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\0': sb.Append("\\0"); break;
                    case '\a': sb.Append("\\a"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\v': sb.Append("\\v"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\'': sb.Append("\\'"); break;
                    case '"': sb.Append("\\\""); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Unescape(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\\' || i == text.Length - 1)
                {
                    sb.Append(c);
                    continue;
                }
                char next = text[++i];
                switch (next)
                {
                    case '0': sb.Append('\0'); break;
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 't': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'v': sb.Append('\v'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'r': sb.Append('\r'); break;
                    case '\'': sb.Append('\''); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append('\\').Append(next); break;
                }
            }
            return sb.ToString();
        }
    }
}
