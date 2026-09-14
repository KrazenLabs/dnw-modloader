using System;
using UnityEngine;

namespace DnWModLoader
{
    // Keeps the overlay temporarily closed after scene load to avoid crashes
    internal sealed class OverlayOpenDelay
    {
        private readonly Func<float> _seconds;
        private float _sceneLoadedAt = float.NegativeInfinity;

        public OverlayOpenDelay(Func<float> seconds)
        {
            _seconds = seconds;
        }

        // Open overlay once delay has passed
        public bool OpenRequested { get; private set; }

        public float SecondsRemaining
        {
            get { return Mathf.Max(0f, _sceneLoadedAt + Mathf.Max(0f, _seconds()) - Time.realtimeSinceStartup); }
        }

        public bool Active
        {
            get { return SecondsRemaining > 0f; }
        }

        public void NoteSceneLoaded()
        {
            _sceneLoadedAt = Time.realtimeSinceStartup;
        }

        public void RequestOpen()
        {
            OpenRequested = true;
        }

        public void CancelRequest()
        {
            OpenRequested = false;
        }

        public bool TakeDueRequest()
        {
            if (!OpenRequested || Active) return false;
            OpenRequested = false;
            return true;
        }
    }
}
