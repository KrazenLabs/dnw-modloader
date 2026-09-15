using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace BepInEx.Configuration
{
    public struct KeyboardShortcut
    {
        public static readonly KeyboardShortcut Empty;

        [Obsolete("Use UnityInput.Current.SupportedKeyCodes instead")]
        public static readonly IEnumerable<KeyCode> AllKeyCodes;

        private static readonly char[] Separators = { ' ', '+', ',', ';', '|' };
        private static KeyCode[] _blockingKeys;

        private readonly KeyCode[] _allKeys;

        static KeyboardShortcut()
        {
            Empty = default(KeyboardShortcut);
            AllKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            TomlTypeConverter.AddConverter(typeof(KeyboardShortcut), new TypeConverter
            {
                ConvertToString = (o, t) => ((KeyboardShortcut)o).Serialize(),
                ConvertToObject = (s, t) => Deserialize(s),
            });
        }

        public KeyboardShortcut(KeyCode mainKey, params KeyCode[] modifiers) : this(new[] { mainKey }.Concat(modifiers).ToArray())
        {
            if (mainKey == KeyCode.None && modifiers.Any())
                throw new ArgumentException("Can't set mainKey to KeyCode.None if there are any modifiers");
        }

        private KeyboardShortcut(KeyCode[] keys)
        {
            _allKeys = Sanitize(keys);
        }

        public KeyCode MainKey { get { return _allKeys != null && _allKeys.Length > 0 ? _allKeys[0] : KeyCode.None; } }

        public IEnumerable<KeyCode> Modifiers { get { return _allKeys?.Skip(1) ?? Enumerable.Empty<KeyCode>(); } }

        private static KeyCode[] Sanitize(KeyCode[] keys)
        {
            if (keys.Length == 0 || keys[0] == KeyCode.None) return new[] { KeyCode.None };
            var main = keys[0];
            return new[] { main }.Concat(keys.Skip(1).Distinct().Where(k => k != main).OrderBy(k => (int)k)).ToArray();
        }

        public static KeyboardShortcut Deserialize(string str)
        {
            try
            {
                var keys = str.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => (KeyCode)Enum.Parse(typeof(KeyCode), x)).ToArray();
                return new KeyboardShortcut(keys);
            }
            catch (SystemException e)
            {
                Logging.Logger.Log(LogLevel.Error, "Failed to read keybind from settings: " + e.Message);
                return Empty;
            }
        }

        public string Serialize()
        {
            return _allKeys == null ? string.Empty : string.Join(" + ", _allKeys.Select(k => k.ToString()).ToArray());
        }

        public bool IsDown()
        {
            var main = MainKey;
            return main != KeyCode.None && UnityInput.Current.GetKeyDown(main) && ModifiersMatch();
        }

        public bool IsPressed()
        {
            var main = MainKey;
            return main != KeyCode.None && UnityInput.Current.GetKey(main) && ModifiersMatch();
        }

        public bool IsUp()
        {
            var main = MainKey;
            return main != KeyCode.None && UnityInput.Current.GetKeyUp(main) && ModifiersMatch();
        }

        private bool ModifiersMatch()
        {
            var keys = _allKeys;
            var main = MainKey;
            var input = UnityInput.Current;
            if (!keys.All(k => k == main || input.GetKey(k))) return false;
            if (_blockingKeys == null)
            {
                var ignored = new[] { KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4, KeyCode.Mouse5, KeyCode.Mouse6, KeyCode.None };
                _blockingKeys = input.SupportedKeyCodes.Except(ignored).ToArray();
            }
            return _blockingKeys.All(k => keys.Contains(k) || !input.GetKey(k));
        }

        public override string ToString()
        {
            return MainKey == KeyCode.None ? "Not set" : string.Join(" + ", _allKeys.Select(k => k.ToString()).ToArray());
        }

        public override bool Equals(object obj)
        {
            return obj is KeyboardShortcut other && MainKey == other.MainKey && Modifiers.SequenceEqual(other.Modifiers);
        }

        public override int GetHashCode()
        {
            if (MainKey == KeyCode.None) return 0;
            return _allKeys.Aggregate(_allKeys.Length, (hash, key) => hash * 31 + (int)key);
        }
    }
}
