using System;
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
        private static readonly Action<HostHooks> EarlyUpdateHook = h => h.EarlyUpdate();
        private static readonly Action<HostHooks> UpdateHook = h => h.Update();
        private static readonly Action<HostHooks> FixedUpdateHook = h => h.FixedUpdate();
        private static readonly Action<HostHooks> LateUpdateHook = h => h.LateUpdate();
        private static readonly Action<HostHooks> GuiHook = h => h.OnGUI();

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
            HostHooks.Run(EarlyUpdateHook);
            ResourceWatcher.DispatchPending();
            ModLoader.Dispatch(nameof(Mod.OnUpdate), UpdateAction);
            HostHooks.Run(UpdateHook);
            try { DnWModLoader.Config.ModConfig.FlushPending(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Saving configs failed"); }
            try { DnWModLoader.Config.DeferredSaves.FlushDue(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Saving plugin configs failed"); }
        }

        private void FixedUpdate()
        {
            if (!_started) return;
            ModLoader.Dispatch(nameof(Mod.OnFixedUpdate), FixedUpdateAction);
            HostHooks.Run(FixedUpdateHook);
        }

        private void LateUpdate()
        {
            if (!_started) return;
            try { Overlay?.LateUpdate(); }
            catch (Exception e) { ReportOverlayFailure(e); }
            ModLoader.Dispatch(nameof(Mod.OnLateUpdate), LateUpdateAction);
            HostHooks.Run(LateUpdateHook);
        }

        private void OnGUI()
        {
            if (!_started) return;
            try { Overlay?.OnGUI(); }
            catch (Exception e) when (!ModLoader.IsExitGuiException(e)) { ReportOverlayFailure(e); }
            ModLoader.Dispatch(nameof(Mod.OnGUI), GuiAction);
            HostHooks.Run(GuiHook);
        }

        private void OnApplicationQuit()
        {
            ModLoader.Dispatch(nameof(Mod.OnApplicationQuit), QuitAction);
            try { DnWModLoader.Config.ModConfig.FlushAll(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Saving configs on quit failed"); }
            try { DnWModLoader.Config.DeferredSaves.FlushAll(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Saving plugin configs on quit failed"); }
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
                Overlay?.ReleaseGame();
                Overlay = null;
            }
        }
    }
}
