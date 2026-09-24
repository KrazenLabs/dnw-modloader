using System;
using Tomlet;
using Tomlet.Models;
using UnityEngine;

namespace MelonLoader
{
    internal static class UnityPreferenceMappers
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered) return;
            _registered = true;
            TomletMain.RegisterMapper<Color>(WriteColor, ReadColor);
            TomletMain.RegisterMapper<Color32>(WriteColor32, ReadColor32);
            TomletMain.RegisterMapper<Vector2>(WriteVector2, ReadVector2);
            TomletMain.RegisterMapper<Vector3>(WriteVector3, ReadVector3);
            TomletMain.RegisterMapper<Vector4>(WriteVector4, ReadVector4);
            TomletMain.RegisterMapper<Quaternion>(WriteQuaternion, ReadQuaternion);
            TomletMain.RegisterMapper<Rect>(WriteRect, ReadRect);
        }

        private static TomlValue WriteColor(Color value) { return Floats(value.r * 255f, value.g * 255f, value.b * 255f, value.a * 255f); }

        private static Color ReadColor(TomlValue value)
        {
            var f = ReadFloats(value, 4, "Color");
            return new Color(f[0] / 255f, f[1] / 255f, f[2] / 255f, f[3] / 255f);
        }

        private static TomlValue WriteColor32(Color32 value) { return TomletMain.ValueFrom(new[] { value.r, value.g, value.b, value.a }); }

        private static Color32 ReadColor32(TomlValue value)
        {
            var array = value as TomlArray;
            if (array == null || array.Count != 4) throw new FormatException("Not a valid Color32");
            var b = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                var number = array[i] as TomlLong;
                if (number == null || number.Value < 0 || number.Value > 255) throw new FormatException("Not a valid Color32");
                b[i] = (byte)number.Value;
            }
            return new Color32(b[0], b[1], b[2], b[3]);
        }

        private static TomlValue WriteVector2(Vector2 value) { return Floats(value.x, value.y); }

        private static Vector2 ReadVector2(TomlValue value)
        {
            var f = ReadFloats(value, 2, "Vector2");
            return new Vector2(f[0], f[1]);
        }

        private static TomlValue WriteVector3(Vector3 value) { return Floats(value.x, value.y, value.z); }

        private static Vector3 ReadVector3(TomlValue value)
        {
            var f = ReadFloats(value, 3, "Vector3");
            return new Vector3(f[0], f[1], f[2]);
        }

        private static TomlValue WriteVector4(Vector4 value) { return Floats(value.x, value.y, value.z, value.w); }

        private static Vector4 ReadVector4(TomlValue value)
        {
            var f = ReadFloats(value, 4, "Vector4");
            return new Vector4(f[0], f[1], f[2], f[3]);
        }

        private static TomlValue WriteQuaternion(Quaternion value) { return Floats(value.x, value.y, value.z, value.w); }

        private static Quaternion ReadQuaternion(TomlValue value)
        {
            var f = ReadFloats(value, 4, "Quaternion");
            return new Quaternion(f[0], f[1], f[2], f[3]);
        }

        private static TomlValue WriteRect(Rect value) { return Floats(value.x, value.y, value.width, value.height); }

        private static Rect ReadRect(TomlValue value)
        {
            var f = ReadFloats(value, 4, "Rect");
            return new Rect(f[0], f[1], f[2], f[3]);
        }

        private static TomlValue Floats(params float[] values)
        {
            return TomletMain.ValueFrom(values);
        }

        private static float[] ReadFloats(TomlValue value, int count, string typeName)
        {
            var array = value as TomlArray;
            if (array == null || array.Count != count) throw new FormatException("Not a valid " + typeName);
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                var item = array[i];
                if (item is TomlDouble d) result[i] = (float)d.Value;
                else if (item is TomlLong l) result[i] = l.Value;
                else throw new FormatException("Not a valid " + typeName);
            }
            return result;
        }
    }
}
