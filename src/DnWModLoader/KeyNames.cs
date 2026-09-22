using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DnWModLoader
{
    internal static class KeyNames
    {
        private static readonly Dictionary<string, string> KeyCodeNameFixups = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LeftCtrl"] = "LeftControl",
            ["RightCtrl"] = "RightControl",
            ["ContextMenu"] = "Menu",
            ["PrintScreen"] = "Print",
            ["Enter"] = "Return",
        };

        private static readonly Dictionary<Key, KeyCode> CodesByKey = new Dictionary<Key, KeyCode>();
        private static readonly Dictionary<KeyCode, Key> KeysByCode = new Dictionary<KeyCode, Key>();

        static KeyNames()
        {
            foreach (Key key in Enum.GetValues(typeof(Key)))
            {
                if (key == Key.None) continue;
                if (!Enum.TryParse(KeyCodeName(key), true, out KeyCode code)) continue;
                CodesByKey[key] = code;
                if (!KeysByCode.ContainsKey(code)) KeysByCode[code] = key;
            }
        }

        private static string KeyCodeName(Key key)
        {
            string name = key.ToString();
            if (name.StartsWith("Numpad", StringComparison.Ordinal)) return "Keypad" + name.Substring(6);
            if (name.StartsWith("Digit", StringComparison.Ordinal)) return "Alpha" + name.Substring(5);
            return KeyCodeNameFixups.TryGetValue(name, out var mapped) ? mapped : name;
        }

        public static bool TryToKeyCode(Key key, out KeyCode code)
        {
            return CodesByKey.TryGetValue(key, out code);
        }

        public static bool TryToKey(KeyCode code, out Key key)
        {
            return KeysByCode.TryGetValue(code, out key);
        }

        public static bool TryParse(string text, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (Enum.TryParse(text, true, out key) && key != Key.None) return true;
            if (Enum.TryParse(text, true, out KeyCode code) && TryToKey(code, out key)) return true;
            key = Key.None;
            return false;
        }

        public static string Label(Key key)
        {
            try
            {
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    string display = keyboard[key].displayName;
                    if (display != null && display.Length == 1 && char.IsLetter(display[0])) return display.ToUpperInvariant();
                }
            }
            catch (Exception) { }
            return key.ToString();
        }

        public static string Label(KeyCode code)
        {
            return TryToKey(code, out var key) ? Label(key) : code.ToString();
        }

        public static string Label(string text)
        {
            if (TryParse(text, out var key)) return Label(key);
            return string.IsNullOrEmpty(text) ? "None" : text;
        }
    }
}
