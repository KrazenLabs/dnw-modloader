using System;
using System.Collections.Generic;
using UnityEngine;

namespace DnWModLoader
{
    internal sealed class ModLoaderBehaviour : MonoBehaviour
    {
        private static readonly Action<Mod> UpdateAction = m => m.OnUpdate();
        private static readonly Action<Mod> FixedUpdateAction = m => m.OnFixedUpdate();
        private static readonly Action<Mod> LateUpdateAction = m => m.OnLateUpdate();
        private static readonly Action<Mod> GuiAction = m => m.OnGUI();
        private static readonly Action<Mod> QuitAction = m => m.OnApplicationQuit();

        internal Overlay Overlay { get; private set; }
        private int _overlayFailures;
        private bool _started;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            try { Overlay = new Overlay(ModLoader.Config); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Overlay could not be created"); }
        }

        private void Start()
        {
            _started = true;
            try { ModLoader.GameStarted(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Game start phase failed"); }
            try { if (ModLoader.Config != null && ModLoader.Config.CheckForUpdates) UpdateChecker.Begin(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Update check could not start"); }
            ModLoader.Logger.Debug("Runtime behaviour started (frame " + Time.frameCount + ", scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "); per-frame callbacks are live.");
        }

        private void Update()
        {
            if (!_started) return;
            try { Overlay?.Update(); }
            catch (Exception e) { ReportOverlayFailure(e); }
            ModLoader.Dispatch("OnUpdate", UpdateAction);
            try { DnWModLoader.Config.ModConfig.FlushPending(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Saving configs failed"); }
        }

        private void FixedUpdate()
        {
            if (!_started) return;
            ModLoader.Dispatch("OnFixedUpdate", FixedUpdateAction);
        }

        private void LateUpdate()
        {
            if (!_started) return;
            try { Overlay?.LateUpdate(); }
            catch (Exception e) { ReportOverlayFailure(e); }
            ModLoader.Dispatch("OnLateUpdate", LateUpdateAction);
        }

        private void OnGUI()
        {
            if (!_started) return;
            try { Overlay?.OnGUI(); }
            catch (Exception e) { ReportOverlayFailure(e); }
            ModLoader.Dispatch("OnGUI", GuiAction);
        }

        private void OnApplicationQuit()
        {
            ModLoader.Dispatch("OnApplicationQuit", QuitAction);
            try { DnWModLoader.Config.ModConfig.FlushAll(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Saving configs on quit failed"); }
            ModLoader.Shutdown();
        }

        private void OnDestroy()
        {
            if (ModLoader.Behaviour == this) ModLoader.Behaviour = null;
        }

        private void ReportOverlayFailure(Exception e)
        {
            _overlayFailures++;
            if (_overlayFailures <= 3) ModLoader.Logger.Exception(e, "Overlay error " + _overlayFailures + "/3");
            if (_overlayFailures == 3)
            {
                ModLoader.Logger.Error("Overlay disabled after repeated errors.");
                Overlay = null;
            }
        }
    }

    // Mod failure tracking
    public sealed partial class ModContainer
    {
        private const int FailuresBeforeDisable = 10;
        private Dictionary<string, int> _failures;
        private HashSet<string> _disabledCallbacks;

        internal bool IsCallbackDisabled(string callback)
        {
            return _disabledCallbacks != null && _disabledCallbacks.Contains(callback);
        }

        internal void ResetFailures(string callback)
        {
            if (_failures != null && _failures.Count > 0) _failures.Remove(callback);
        }

        internal void RecordFailure(string callback, Exception e)
        {
            if (_failures == null) _failures = new Dictionary<string, int>();
            _failures.TryGetValue(callback, out int count);
            count++;
            _failures[callback] = count;

            if (count <= 3)
                ModLoader.Logger.Exception(e, "Mod " + Info.Id + " threw in " + callback + " (" + count + ")");

            if (count >= FailuresBeforeDisable)
            {
                if (_disabledCallbacks == null) _disabledCallbacks = new HashSet<string>();
                _disabledCallbacks.Add(callback);
                Error = callback + " disabled after " + count + " consecutive errors: " + e.GetType().Name + ": " + e.Message;
                ModLoader.Logger.Error("Mod " + Info.Id + ": " + callback + " disabled after " + count + " consecutive errors.");
            }
        }
    }
}
