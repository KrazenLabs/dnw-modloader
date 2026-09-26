using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DnWModLoader
{
    public static partial class ModLoader
    {
        private static ModContainer[] _loadedCache = new ModContainer[0];
        private static bool _loadedCacheDirty;
        private static bool _sceneEventsHooked;

        private static void RaiseModsInitialized()
        {
            var handlers = ModsInitialized;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception e) { Logger.Exception(e, "ModsInitialized in " + HandlerOwner(handler) + " threw"); }
            }
        }

        private static void SetPhase(LoaderPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            var handlers = PhaseChanged;
            if (handlers == null) return;
            foreach (Action<LoaderPhase> handler in handlers.GetInvocationList())
            {
                try { handler(phase); }
                catch (Exception e) { Logger.Exception(e, "PhaseChanged in " + HandlerOwner(handler) + " threw"); }
            }
        }

        private static string HandlerOwner(Delegate handler)
        {
            var type = handler.Method.DeclaringType;
            return type == null ? handler.Method.Name : type.Assembly.GetName().Name + " (" + type.FullName + "." + handler.Method.Name + ")";
        }

        internal static void InvalidateLoadedCache()
        {
            _loadedCacheDirty = true;
        }

        private static ModContainer[] LoadedMods
        {
            get
            {
                if (_loadedCacheDirty)
                {
                    _loadedCacheDirty = false;
                    _loadedCache = ModList.Where(c => c.CallbacksEnabled).ToArray();
                }
                return _loadedCache;
            }
        }

        internal static void Dispatch(string callback, Action<Mod> action)
        {
            var mods = LoadedMods;
            for (int i = 0; i < mods.Length; i++)
            {
                var container = mods[i];
                if (container.IsCallbackDisabled(callback)) continue;
                try
                {
                    action(container.Instance);
                }
                catch (Exception e) when (!IsExitGuiException(e))
                {
                    container.RecordFailure(callback, e);
                }
            }
        }

        internal static void DispatchTo(ModContainer container, string callback, Action<Mod> action)
        {
            if (container == null || !container.CallbacksEnabled || container.IsCallbackDisabled(callback)) return;
            try
            {
                action(container.Instance);
            }
            catch (Exception e) when (!IsExitGuiException(e))
            {
                container.RecordFailure(callback, e);
            }
        }

        internal static bool IsExitGuiException(Exception e)
        {
            while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            return e is ExitGUIException;
        }

        internal static void EnsureBehaviour(string phase)
        {
            if (Behaviour != null) return;
            try
            {
                var go = new GameObject("DnWModLoader");
                UnityEngine.Object.DontDestroyOnLoad(go);
                Behaviour = go.AddComponent<ModLoaderBehaviour>();
                Logger.Debug("Runtime behaviour created during " + phase + ".");
            }
            catch (Exception e)
            {
                Behaviour = null;
                Logger.Warning("Could not create runtime behaviour during " + phase + " (will retry later): " + e.Message);
            }
        }

        private static void HookSceneEvents()
        {
            if (_sceneEventsHooked) return;
            _sceneEventsHooked = true;
            try
            {
                SceneManager.sceneLoaded += (scene, mode) =>
                {
                    Logger.Debug("Scene loaded: " + scene.name + " (" + mode + ")");
                    EnsureBehaviour("scene load");
                    Dispatch(nameof(Mod.OnSceneLoaded), m => m.OnSceneLoaded(scene, mode));
                    HostHooks.Run(h => h.SceneLoaded(scene, mode));
                };
                SceneManager.sceneUnloaded += scene =>
                {
                    Logger.Debug("Scene unloaded: " + scene.name);
                    Dispatch(nameof(Mod.OnSceneUnloaded), m => m.OnSceneUnloaded(scene));
                    HostHooks.Run(h => h.SceneUnloaded(scene));
                };
            }
            catch (Exception e)
            {
                Logger.Warning("Could not subscribe to scene events: " + e.Message);
            }
        }
    }
}
