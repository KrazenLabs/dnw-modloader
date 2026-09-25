using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace DnWModLoader
{
    internal abstract class HostHooks
    {
        private const int FailuresLoggedInFull = 3;
        private static readonly List<HostHooks> All = new List<HostHooks>();

        private int _failures;

        public abstract string Name { get; }

        public virtual void GameStarted() { }
        public virtual void EarlyUpdate() { }
        public virtual void Update() { }
        public virtual void FixedUpdate() { }
        public virtual void LateUpdate() { }
        public virtual void OnGUI() { }
        public virtual void SceneLoaded(Scene scene, LoadSceneMode mode) { }
        public virtual void SceneUnloaded(Scene scene) { }

        internal static void Add(HostHooks hooks)
        {
            if (hooks != null && !All.Contains(hooks)) All.Add(hooks);
        }

        internal static void Run(Action<HostHooks> action)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var hooks = All[i];
                try
                {
                    action(hooks);
                }
                catch (Exception e) when (!ModLoader.IsExitGuiException(e))
                {
                    hooks._failures++;
                    if (hooks._failures <= FailuresLoggedInFull) ModLoader.Logger.Exception(e, hooks.Name + " support failed (" + hooks._failures + ")");
                }
            }
        }
    }
}
