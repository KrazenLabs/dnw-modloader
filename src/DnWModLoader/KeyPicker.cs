using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DnWModLoader
{
    internal sealed class KeyPicker
    {
        private const string Prompt = "Press a key...";

        private string _listeningId;
        private bool _useLegacyEvents;

        public bool Listening { get { return _listeningId != null; } }

        public bool IsListening(string id)
        {
            return _listeningId != null && _listeningId == id;
        }

        public void Begin(string id)
        {
            _listeningId = id;
            GUI.FocusControl(null);
        }

        public void Cancel()
        {
            _listeningId = null;
        }

        public bool Draw(string id, string label, float width, out Key picked)
        {
            picked = Key.None;
            bool pickedNow = false;
            var e = Event.current;
            if (IsListening(id))
            {
                if (TryRead(e, out picked, out bool cancelled))
                {
                    pickedNow = true;
                    _listeningId = null;
                }
                else if (cancelled) _listeningId = null;
                if (e.type == EventType.KeyDown || e.type == EventType.KeyUp) e.Use();
            }

            bool listening = IsListening(id);
            var content = new GUIContent(listening ? Prompt : label);
            var rect = GUILayoutUtility.GetRect(content, GUI.skin.button, GUILayout.Width(width));
            if (listening && e.type == EventType.MouseDown && !rect.Contains(e.mousePosition))
            {
                _listeningId = null;
                listening = false;
            }
            if (GUI.Button(rect, content))
            {
                if (listening) Cancel();
                else Begin(id);
            }
            return pickedNow;
        }

        private bool TryRead(Event e, out Key key, out bool cancelled)
        {
            key = Key.None;
            cancelled = false;
            if (!_useLegacyEvents)
            {
                if (e.type != EventType.Layout) return false;
                try
                {
                    if (GameInputBlock.Capturing)
                    {
                        if (GameInputBlock.WasPressedThisFrame(Key.Escape))
                        {
                            cancelled = true;
                            return false;
                        }
                        return GameInputBlock.TryGetPressedThisFrame(out key);
                    }
                    var keyboard = Keyboard.current;
                    if (keyboard == null) return false;
                    if (keyboard.escapeKey.wasPressedThisFrame)
                    {
                        cancelled = true;
                        return false;
                    }
                    foreach (var control in keyboard.allKeys)
                    {
                        if (!control.wasPressedThisFrame) continue;
                        var candidate = control.keyCode;
                        if (candidate == Key.None) continue;
                        key = candidate;
                        return true;
                    }
                    return false;
                }
                catch (Exception error)
                {
                    _useLegacyEvents = true;
                    ModLoader.Logger.Debug("Input System unavailable for the key picker (" + error.Message + "); using IMGUI key events instead.");
                    return false;
                }
            }

            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return false;
            if (e.keyCode == KeyCode.Escape)
            {
                cancelled = true;
                return false;
            }
            return KeyNames.TryToKey(e.keyCode, out key);
        }
    }
}
