using System;
using System.Reflection;
using UnityEngine;

namespace DnWModLoader
{
    // Frees the mouse cursor while the overlay is open
    internal sealed class GameCursor
    {
        private static bool _reflected;
        private static MethodInfo _request;
        private static MethodInfo _release;

        private object _ticket;
        private bool _unlocked;
        private bool _warned;

        public void Unlock()
        {
            _unlocked = true;
            _ticket = RequestTicket();
        }

        public void Restore()
        {
            _unlocked = false;
            ReleaseTicket();
        }

        public void KeepUnlocked()
        {
            if (!_unlocked || _ticket != null) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        private object RequestTicket()
        {
            Reflect();
            if (_request == null || ModLoader.Behaviour == null) return null;
            try
            {
                return _request.Invoke(null, new object[] { ModLoader.Behaviour, true });
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    ModLoader.Logger.Debug("Cursor unlock via GameStateManager failed (" + (e.InnerException ?? e).Message + "); forcing the cursor free instead.");
                }
                return null;
            }
        }

        private void ReleaseTicket()
        {
            if (_ticket == null) return;
            var ticket = _ticket;
            _ticket = null;
            if (_release == null) return;
            try
            {
                _release.Invoke(null, new[] { ticket });
            }
            catch (Exception e)
            {
                ModLoader.Logger.Debug("Releasing the cursor ticket failed: " + (e.InnerException ?? e).Message);
            }
        }

        private static void Reflect()
        {
            if (_reflected) return;
            _reflected = true;
            try
            {
                var type = Type.GetType("GameStateManager, Assembly-CSharp", false);
                if (type == null) return;
                _request = type.GetMethod("RequestCursorUnlock", BindingFlags.Public | BindingFlags.Static);
                _release = type.GetMethod("ReleaseCursorUnlock", BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception e)
            {
                ModLoader.Logger.Debug("GameStateManager cursor API not available: " + e.Message);
            }
        }
    }
}
