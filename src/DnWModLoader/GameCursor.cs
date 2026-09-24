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
        private static PropertyInfo _instance;

        private object _ticket;
        private bool _unlocked;
        private bool _failed;

        public void Unlock()
        {
            _unlocked = true;
            TakeTicket();
        }

        public void Restore()
        {
            _unlocked = false;
            ReleaseTicket();
        }

        public void KeepUnlocked()
        {
            if (!_unlocked) return;
            if (_ticket == null) TakeTicket();
            if (_ticket != null) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        private void TakeTicket()
        {
            if (_ticket != null || _failed) return;
            Reflect();
            if (_request == null || _instance == null || ModLoader.Behaviour == null) return;
            try
            {
                if (_instance.GetValue(null, null) == null) return;
                _ticket = _request.Invoke(null, new object[] { ModLoader.Behaviour, true });
            }
            catch (Exception e)
            {
                _failed = true;
                ModLoader.Logger.Debug("Cursor unlock via GameStateManager failed (" + (e.InnerException ?? e).Message + ")");
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
                _instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception e)
            {
                ModLoader.Logger.Debug("GameStateManager cursor API not available: " + e.Message);
            }
        }
    }
}
