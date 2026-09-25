using System;
using System.Globalization;
using System.Reflection;

namespace DnWModLoader.Config
{
    internal static class SettingValues
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static bool TryParse(string text, Type type, Func<string, object> parseOther, out object value, out string error)
        {
            value = null;
            error = null;
            text = text ?? "";
            try
            {
                if (type == typeof(string))
                {
                    value = text;
                    return true;
                }
                string trimmed = text.Trim();
                if (type == typeof(bool)) value = ParseBool(trimmed);
                else if (type.IsEnum) value = Enum.Parse(type, trimmed, true);
                else if (ConfigEntryBase.IsIntegerType(type)) return TryParseWholeNumber(trimmed, type, out value, out error);
                else if (ConfigEntryBase.IsNumericType(type)) return TryParseNumber(trimmed, type, out value, out error);
                else value = parseOther(text);
                return true;
            }
            catch (Exception e)
            {
                error = (e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).Message;
                return false;
            }
        }

        public static string Format(object value, Func<object, string> formatOther)
        {
            if (value == null) return "";
            if (value is float f) return f.ToString("0.###", Invariant);
            if (value is double d) return d.ToString("0.####", Invariant);
            if (value is string || value is Enum) return value.ToString();
            if (value is IConvertible convertible) return convertible.ToString(Invariant);
            return formatOther(value);
        }

        public static object Coerce(object value, Type type)
        {
            if (value == null || type.IsInstanceOfType(value)) return value;
            if (type.IsEnum) return value is string s ? Enum.Parse(type, s, true) : Enum.ToObject(type, value);
            if (value is IConvertible) return Convert.ChangeType(value, type, Invariant);
            return value;
        }

        private static bool ParseBool(string text)
        {
            string t = text.ToLowerInvariant();
            if (t == "1" || t == "on" || t == "yes") return true;
            if (t == "0" || t == "off" || t == "no") return false;
            return bool.Parse(t);
        }

        private static bool TryParseWholeNumber(string text, Type type, out object value, out string error)
        {
            value = null;
            error = null;
            decimal number;
            if (!decimal.TryParse(text, NumberStyles.Integer, Invariant, out number))
            {
                error = "\"" + text + "\" is not a whole number";
                return false;
            }
            try
            {
                value = Convert.ChangeType(number, type, Invariant);
                return true;
            }
            catch (OverflowException)
            {
                error = "\"" + text + "\" is out of range (" + Limit(type, "MinValue") + " to " + Limit(type, "MaxValue") + ")";
                return false;
            }
        }

        private static bool TryParseNumber(string text, Type type, out object value, out string error)
        {
            value = null;
            error = null;
            string normalized = WithDecimalPoint(text);
            bool parsed;
            if (type == typeof(float))
            {
                float f;
                parsed = float.TryParse(normalized, NumberStyles.Float, Invariant, out f) && !float.IsNaN(f);
                value = f;
            }
            else if (type == typeof(double))
            {
                double d;
                parsed = double.TryParse(normalized, NumberStyles.Float, Invariant, out d) && !double.IsNaN(d);
                value = d;
            }
            else
            {
                decimal m;
                parsed = decimal.TryParse(normalized, NumberStyles.Float, Invariant, out m);
                value = m;
            }
            if (parsed) return true;
            value = null;
            error = "\"" + text + "\" is not a number";
            return false;
        }

        private static string WithDecimalPoint(string text)
        {
            int comma = text.IndexOf(',');
            if (comma < 0 || text.IndexOf('.') >= 0 || text.IndexOf(',', comma + 1) >= 0) return text;
            return text.Replace(',', '.');
        }

        private static string Limit(Type type, string field)
        {
            var limit = type.GetField(field, BindingFlags.Public | BindingFlags.Static);
            return limit != null ? Convert.ToString(limit.GetValue(null), Invariant) : "?";
        }
    }
}
