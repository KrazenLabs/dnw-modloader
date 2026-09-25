using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace DnWModLoader
{
    internal static class GameInputBlock
    {
        private static readonly HashSet<Key> Down = new HashSet<Key>();
        private static readonly List<Key> Pressed = new List<Key>();
        private static bool _wanted;
        private static bool _active;
        private static bool _failed;
        private static int _pressedFrame = -1;
        private static int _heldBack;

        public static bool Capturing { get { return _active; } }

        public static void Block()
        {
            _wanted = true;
        }

        public static void Maintain()
        {
            if (!_wanted || _active || _failed) return;
            try
            {
                Down.Clear();
                Pressed.Clear();
                _heldBack = 0;
                var keyboard = Keyboard.current;
                if (keyboard != null)
                    foreach (var control in keyboard.allKeys)
                        if (control.isPressed) Down.Add(control.keyCode);
                foreach (var device in InputSystem.devices)
                    if (device is Keyboard || device is Pointer || device is Gamepad || device is Joystick) InputSystem.ResetDevice(device);
                InputSystem.onEvent += OnEvent;
                _active = true;
            }
            catch (Exception e)
            {
                _failed = true;
                ModLoader.Logger.Warning("Could not block game input: " + e.Message);
            }
        }

        public static void Restore()
        {
            _wanted = false;
            _failed = false;
            if (!_active) return;
            _active = false;
            try { InputSystem.onEvent -= OnEvent; }
            catch (Exception e) { ModLoader.Logger.Debug("Could not release game input: " + e.Message); }
            Down.Clear();
            Pressed.Clear();
            ModLoader.Logger.Debug("Game input released; " + _heldBack + " input event(s) blocked");
        }

        public static bool WasPressedThisFrame(Key key)
        {
            return _pressedFrame == Time.frameCount && Pressed.Contains(key);
        }

        public static bool TryGetPressedThisFrame(out Key key)
        {
            key = Key.None;
            if (_pressedFrame != Time.frameCount || Pressed.Count == 0) return false;
            key = Pressed[0];
            return true;
        }

        private static void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!_active) return;
            bool state = eventPtr.IsA<StateEvent>() || eventPtr.IsA<DeltaStateEvent>();
            if (!state && !eventPtr.IsA<TextEvent>() && !eventPtr.IsA<IMECompositionEvent>()) return;
            if (state && device is Keyboard keyboard) Capture(keyboard, eventPtr);
            eventPtr.handled = true;
            _heldBack++;
        }

        private static void Capture(Keyboard keyboard, InputEventPtr eventPtr)
        {
            foreach (KeyControl control in keyboard.allKeys)
            {
                float value;
                if (!control.ReadValueFromEvent(eventPtr, out value)) continue;
                var key = control.keyCode;
                if (key == Key.None) continue;
                if (value >= control.pressPointOrDefault)
                {
                    if (!Down.Add(key)) continue;
                    if (_pressedFrame != Time.frameCount)
                    {
                        Pressed.Clear();
                        _pressedFrame = Time.frameCount;
                    }
                    Pressed.Add(key);
                }
                else
                {
                    Down.Remove(key);
                }
            }
        }
    }
}
