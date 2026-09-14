using UnityEngine;
using UnityEngine.SceneManagement;

namespace DnWModLoader
{
    // Schedules one invisible draw of the whole overlay to initialize IMGUI assets
    internal sealed class OverlayWarmup
    {
        private const float DelaySeconds = 3f;

        private readonly int _frameCount;
        private string _firstScene;
        private float _sceneLoadedAt;
        private int _framesDrawn;

        public OverlayWarmup(int frameCount)
        {
            _frameCount = frameCount;
        }

        public bool Done { get; private set; }

        public void NoteSceneLoaded(string sceneName)
        {
            if (_firstScene == null) _firstScene = sceneName;
            _sceneLoadedAt = Time.realtimeSinceStartup;
            _framesDrawn = 0;   // stop warmup on scene load
        }

        public int FrameDue(bool overlayVisible)
        {
            if (Done || overlayVisible || _firstScene == null) return -1;
            if (Time.realtimeSinceStartup - _sceneLoadedAt < DelaySeconds) return -1;
            if (SceneManager.GetActiveScene().name != _firstScene) return -1;
            return _framesDrawn;
        }

        public void FrameRepainted()
        {
            _framesDrawn++;
            if (_framesDrawn >= _frameCount) Done = true;
        }
    }
}
