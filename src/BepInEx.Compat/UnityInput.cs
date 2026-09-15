using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BepInEx
{
    public interface IInputSystem
    {
        Vector3 mousePosition { get; }
        Vector2 mouseScrollDelta { get; }
        bool mousePresent { get; }
        bool anyKey { get; }
        bool anyKeyDown { get; }
        IEnumerable<KeyCode> SupportedKeyCodes { get; }
        bool GetKey(string name);
        bool GetKey(KeyCode key);
        bool GetKeyDown(string name);
        bool GetKeyDown(KeyCode key);
        bool GetKeyUp(string name);
        bool GetKeyUp(KeyCode key);
        bool GetMouseButton(int button);
        bool GetMouseButtonDown(int button);
        bool GetMouseButtonUp(int button);
        void ResetInputAxes();
    }

    // Legacy Input when the game requires it, otherwise Input System package
    public static class UnityInput
    {
        private static IInputSystem _current;

        public static IInputSystem Current
        {
            get
            {
                if (_current != null) return _current;
                try
                {
                    Input.GetKeyDown(KeyCode.A);
                    _current = new LegacyInputSystem();
                }
                catch (InvalidOperationException)
                {
                    _current = new PackageInputSystem();
                }
                Logging.Logger.LogDebug("[UnityInput] Using " + _current.GetType().Name);
                return _current;
            }
        }

        public static bool LegacyInputSystemAvailable { get { return Current is LegacyInputSystem; } }
    }

    internal sealed class LegacyInputSystem : IInputSystem
    {
        private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        public Vector3 mousePosition { get { return Input.mousePosition; } }
        public Vector2 mouseScrollDelta { get { return Input.mouseScrollDelta; } }
        public bool mousePresent { get { return Input.mousePresent; } }
        public bool anyKey { get { return Input.anyKey; } }
        public bool anyKeyDown { get { return Input.anyKeyDown; } }
        public IEnumerable<KeyCode> SupportedKeyCodes { get { return AllKeys; } }
        public bool GetKey(string name) { return Input.GetKey(name); }
        public bool GetKey(KeyCode key) { return Input.GetKey(key); }
        public bool GetKeyDown(string name) { return Input.GetKeyDown(name); }
        public bool GetKeyDown(KeyCode key) { return Input.GetKeyDown(key); }
        public bool GetKeyUp(string name) { return Input.GetKeyUp(name); }
        public bool GetKeyUp(KeyCode key) { return Input.GetKeyUp(key); }
        public bool GetMouseButton(int button) { return Input.GetMouseButton(button); }
        public bool GetMouseButtonDown(int button) { return Input.GetMouseButtonDown(button); }
        public bool GetMouseButtonUp(int button) { return Input.GetMouseButtonUp(button); }
        public void ResetInputAxes() { Input.ResetInputAxes(); }
    }

    // Maps legacy KeyCodes
    internal sealed class PackageInputSystem : IInputSystem
    {
        private static readonly Dictionary<string, string> KeyNameToKeyCode = new Dictionary<string, string>
        {
            ["LeftCtrl"] = "LeftControl",
            ["RightCtrl"] = "RightControl",
            ["LeftMeta"] = "LeftApple",
            ["RightMeta"] = "RightApple",
            ["ContextMenu"] = "Menu",
            ["PrintScreen"] = "Print",
            ["Enter"] = "Return",
        };

        private static readonly Dictionary<KeyCode, Key> KeyMap = BuildKeyMap();

        private static Dictionary<KeyCode, Key> BuildKeyMap()
        {
            var map = new Dictionary<KeyCode, Key>();
            foreach (Key key in Enum.GetValues(typeof(Key)))
            {
                if (key == Key.None) continue;
                string name = key.ToString();
                if (name.StartsWith("Numpad")) name = "Keypad" + name.Substring(6);
                else if (name.StartsWith("Digit")) name = "Alpha" + name.Substring(5);
                else if (KeyNameToKeyCode.TryGetValue(name, out var mapped)) name = mapped;
                try
                {
                    var code = (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
                    if (!map.ContainsKey(code)) map[code] = key;
                }
                catch (ArgumentException) { }
            }
            return map;
        }

        public Vector3 mousePosition { get { var mouse = Mouse.current; return mouse != null ? (Vector3)mouse.position.ReadValue() : Vector3.zero; } }
        public Vector2 mouseScrollDelta { get { var mouse = Mouse.current; return mouse != null ? mouse.scroll.ReadValue() : Vector2.zero; } }
        public bool mousePresent { get { var mouse = Mouse.current; return mouse != null && mouse.enabled; } }

        public bool anyKey
        {
            get
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.anyKey.isPressed) return true;
                for (int i = 0; i < 5; i++) if (GetMouseButton(i)) return true;
                return false;
            }
        }

        public bool anyKeyDown
        {
            get
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;
                for (int i = 0; i < 5; i++) if (GetMouseButtonDown(i)) return true;
                return false;
            }
        }

        public IEnumerable<KeyCode> SupportedKeyCodes { get { return KeyMap.Keys.ToArray(); } }

        public bool GetKey(string name) { return GetKey(ParseKeyCode(name)); }
        public bool GetKeyDown(string name) { return GetKeyDown(ParseKeyCode(name)); }
        public bool GetKeyUp(string name) { return GetKeyUp(ParseKeyCode(name)); }

        public bool GetKey(KeyCode key) { var control = Control(key); return control != null && control.isPressed; }
        public bool GetKeyDown(KeyCode key) { var control = Control(key); return control != null && control.wasPressedThisFrame; }
        public bool GetKeyUp(KeyCode key) { var control = Control(key); return control != null && control.wasReleasedThisFrame; }

        public bool GetMouseButton(int button) { return GetKey(KeyCode.Mouse0 + button); }
        public bool GetMouseButtonDown(int button) { return GetKeyDown(KeyCode.Mouse0 + button); }
        public bool GetMouseButtonUp(int button) { return GetKeyUp(KeyCode.Mouse0 + button); }

        public void ResetInputAxes() { }

        private static KeyCode ParseKeyCode(string name)
        {
            return (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
        }

        private static ButtonControl Control(KeyCode code)
        {
            if (code >= KeyCode.Mouse0 && code <= KeyCode.Mouse6)
            {
                var mouse = Mouse.current;
                if (mouse == null) return null;
                switch (code)
                {
                    case KeyCode.Mouse0: return mouse.leftButton;
                    case KeyCode.Mouse1: return mouse.rightButton;
                    case KeyCode.Mouse2: return mouse.middleButton;
                    case KeyCode.Mouse3: return mouse.backButton;
                    case KeyCode.Mouse4: return mouse.forwardButton;
                    default: return null;
                }
            }
            var keyboard = Keyboard.current;
            if (keyboard == null || !KeyMap.TryGetValue(code, out var key)) return null;
            return keyboard[key];
        }
    }
}
