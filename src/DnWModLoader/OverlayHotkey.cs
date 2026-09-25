using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DnWModLoader
{
    internal sealed class OverlayHotkey
    {
        private Key _key = Key.F10;
        private KeyCode _legacyKey = KeyCode.F10;
        private bool _useLegacyEvents;

        public OverlayHotkey(string name)
        {
            Set(name);
        }

        // Key name
        public string Name { get; private set; }

        public string Label { get { return _useLegacyEvents ? Name : KeyNames.Label(_key); } }

        public void Set(string name)
        {
            string text = string.IsNullOrEmpty(name) ? "F10" : name.Trim();
            if (!KeyNames.TryParse(text, out _key))
            {
                ModLoader.Logger.Warning("Unknown overlay hotkey \"" + text + "\" in " + LoaderConfig.FileName + "; falling back to F10.");
                _key = Key.F10;
            }
            if (!KeyNames.TryToKeyCode(_key, out _legacyKey)) _legacyKey = KeyCode.F10;
            Name = _key.ToString();
        }

        public bool PressedThisFrame()
        {
            if (_useLegacyEvents) return false;
            try
            {
                if (GameInputBlock.Capturing) return GameInputBlock.WasPressedThisFrame(_key);
                var keyboard = Keyboard.current;
                return keyboard != null && keyboard[_key].wasPressedThisFrame;
            }
            catch (Exception e)
            {
                _useLegacyEvents = true;
                ModLoader.Logger.Debug("Input System unavailable for the overlay hotkey (" + e.Message + "); using IMGUI key events instead.");
                return false;
            }
        }

        // Fallback
        public bool IsKeyDown(Event e)
        {
            return _useLegacyEvents && e != null && e.type == EventType.KeyDown && e.keyCode == _legacyKey;
        }
    }
}
