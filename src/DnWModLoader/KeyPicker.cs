using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DnWModLoader
{
    internal sealed class KeyPicker
    {
        private const string Prompt = "Press a key...";

        private static readonly Key[] ModifierKeys = { Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.LeftMeta, Key.RightMeta };
        private static readonly Key[] NoModifiers = new Key[0];

        private readonly HashSet<Key> _pendingModifiers = new HashSet<Key>();
        private string _listeningId;
        private bool _combinations;
        private int _shownFrame;
        private Rect _screenRect;
        private bool _useLegacyEvents;

        public bool Listening { get { return _listeningId != null; } }

        public bool IsListening(string id)
        {
            return _listeningId != null && _listeningId == id;
        }

        public void Cancel()
        {
            _listeningId = null;
            _pendingModifiers.Clear();
        }

        public void CancelIfHidden()
        {
            if (_listeningId != null && Time.frameCount - _shownFrame > 1) Cancel();
        }

        public void CancelUnlessOver(Vector2 screenPosition)
        {
            if (_listeningId != null && !_screenRect.Contains(screenPosition)) Cancel();
        }

        public bool Draw(string id, string label, float width, bool combinations, out Key picked, out Key[] modifiers)
        {
            picked = Key.None;
            modifiers = NoModifiers;
            bool pickedNow = false;
            var e = Event.current;
            if (IsListening(id))
            {
                _shownFrame = Time.frameCount;
                if (GUIUtility.keyboardControl != 0)
                {
                    Cancel();
                }
                else
                {
                    if (TryRead(e, out picked, out modifiers, out bool cancelled))
                    {
                        pickedNow = true;
                        Cancel();
                    }
                    else if (cancelled) Cancel();
                    if (e.type == EventType.KeyDown || e.type == EventType.KeyUp) e.Use();
                }
            }

            bool listening = IsListening(id);
            var content = new GUIContent(listening ? Prompt : label);
            var rect = GUILayoutUtility.GetRect(content, GUI.skin.button, GUILayout.Width(width));
            if (listening && e.type == EventType.Repaint) _screenRect = GUIUtility.GUIToScreenRect(rect);
            if (GUI.Button(rect, content))
            {
                if (listening) Cancel();
                else Begin(id, combinations, rect);
            }
            return pickedNow;
        }

        private void Begin(string id, bool combinations, Rect rect)
        {
            _listeningId = id;
            _combinations = combinations;
            _pendingModifiers.Clear();
            _shownFrame = Time.frameCount;
            _screenRect = GUIUtility.GUIToScreenRect(rect);
            GUI.FocusControl(null);
        }

        private bool TryRead(Event e, out Key key, out Key[] modifiers, out bool cancelled)
        {
            key = Key.None;
            modifiers = NoModifiers;
            cancelled = false;
            if (!_useLegacyEvents)
            {
                if (e.type != EventType.Layout) return false;
                try
                {
                    return ReadInputSystem(out key, out modifiers, out cancelled);
                }
                catch (Exception error)
                {
                    _useLegacyEvents = true;
                    ModLoader.Logger.Debug("Input System unavailable for the key picker (" + error.Message + "); using IMGUI key events instead.");
                    return false;
                }
            }
            return ReadEvent(e, out key, out modifiers, out cancelled);
        }

        private bool ReadInputSystem(out Key key, out Key[] modifiers, out bool cancelled)
        {
            key = Key.None;
            modifiers = NoModifiers;
            cancelled = false;
            IReadOnlyList<Key> pressed;
            Func<Key, bool> isDown;
            if (GameInputBlock.Capturing)
            {
                pressed = GameInputBlock.KeysPressedThisFrame;
                isDown = GameInputBlock.IsDown;
            }
            else
            {
                var keyboard = Keyboard.current;
                if (keyboard == null) return false;
                var list = new List<Key>();
                foreach (var control in keyboard.allKeys)
                    if (control.wasPressedThisFrame && control.keyCode != Key.None) list.Add(control.keyCode);
                pressed = list;
                isDown = k => keyboard[k].isPressed;
            }

            for (int i = 0; i < pressed.Count; i++)
            {
                if (pressed[i] != Key.Escape) continue;
                cancelled = true;
                return false;
            }
            for (int i = 0; i < pressed.Count; i++)
            {
                var candidate = pressed[i];
                if (candidate == Key.None) continue;
                if (_combinations && IsModifier(candidate))
                {
                    _pendingModifiers.Add(candidate);
                    continue;
                }
                key = candidate;
                modifiers = _combinations ? HeldModifiers(isDown, candidate) : NoModifiers;
                return true;
            }
            foreach (var modifier in _pendingModifiers)
            {
                if (isDown(modifier)) continue;
                key = modifier;
                modifiers = HeldModifiers(isDown, modifier);
                return true;
            }
            return false;
        }

        private bool ReadEvent(Event e, out Key key, out Key[] modifiers, out bool cancelled)
        {
            key = Key.None;
            modifiers = NoModifiers;
            cancelled = false;
            if (e.keyCode == KeyCode.None) return false;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    cancelled = true;
                    return false;
                }
                if (!KeyNames.TryToKey(e.keyCode, out var candidate)) return false;
                if (_combinations && IsModifier(candidate))
                {
                    _pendingModifiers.Add(candidate);
                    return false;
                }
                key = candidate;
                if (_combinations) modifiers = ModifiersOf(e.modifiers);
                return true;
            }
            if (e.type == EventType.KeyUp && _combinations && KeyNames.TryToKey(e.keyCode, out var released) && _pendingModifiers.Contains(released))
            {
                key = released;
                return true;
            }
            return false;
        }

        private static bool IsModifier(Key key)
        {
            return Array.IndexOf(ModifierKeys, key) >= 0;
        }

        private static Key[] HeldModifiers(Func<Key, bool> isDown, Key except)
        {
            var held = new List<Key>();
            foreach (var modifier in ModifierKeys)
                if (modifier != except && isDown(modifier)) held.Add(modifier);
            return held.Count == 0 ? NoModifiers : held.ToArray();
        }

        private static Key[] ModifiersOf(EventModifiers flags)
        {
            var held = new List<Key>();
            if ((flags & EventModifiers.Control) != 0) held.Add(Key.LeftCtrl);
            if ((flags & EventModifiers.Shift) != 0) held.Add(Key.LeftShift);
            if ((flags & EventModifiers.Alt) != 0) held.Add(Key.LeftAlt);
            if ((flags & EventModifiers.Command) != 0) held.Add(Key.LeftMeta);
            return held.Count == 0 ? NoModifiers : held.ToArray();
        }
    }
}
