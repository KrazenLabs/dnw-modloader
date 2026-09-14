using System;
using System.Reflection;
using DnWModLoader.Config;
using DnWModLoader.Logging;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace DnWModLoader
{
    /// <summary>
    /// Base class for a mod's entry point. Create exactly one non-abstract subclass per mod assembly and
    /// describe the mod either in <c>mod.json</c> next to the DLL or with <see cref="ModInfoAttribute"/>
    /// </summary>
    public abstract class Mod
    {
        public ModInfo Info { get; internal set; }

        public ModLogger Logger { get; internal set; }

        public ModConfig Config { get; internal set; }

        // Directory that contains the mod (or the Mods folder for bare DLLs)
        public string Directory { get { return Info?.Directory; } }

        private Harmony _harmony;

        // Harmony instance whose id is the mod id, created on first access
        public Harmony Harmony
        {
            get
            {
                if (_harmony == null) _harmony = new Harmony(Info != null ? Info.Id : GetType().FullName);
                return _harmony;
            }
        }

        /// <summary>
        /// When true (default) the loader calls <c>Harmony.PatchAll(assembly)</c> right after <see cref="OnInitialize"/>,
        /// which applies every class annotated with <c>[HarmonyPatch]</c> in the mod assembly
        /// </summary>
        public virtual bool AutoPatch { get { return true; } }

        // Bind config entries and set up patches here, do not touch scene objects yet
        public virtual void OnInitialize() { }

        // Called after every mod's OnInitialize. You can look up other mods here
        public virtual void OnAllModsInitialized() { }

        // Called only once, on the first frame after the first scene loads
        public virtual void OnGameStarted() { }

        // Called on every scene load
        public virtual void OnSceneLoaded(Scene scene, LoadSceneMode mode) { }

        // Called on scene unload
        public virtual void OnSceneUnloaded(Scene scene) { }

        // Called every frame (equivalent to Unity's update)
        public virtual void OnUpdate() { }

        // Called every physics update (equivalent to Unity's FixedUpdate)
        public virtual void OnFixedUpdate() { }

        // Called every frame after all other updates (equivalent to Unity's LateUpdate)
        public virtual void OnLateUpdate() { }

        // Called on UI updates (equivalent to Unity's OnGUI)
        public virtual void OnGUI() { }

        // Called just before the game closes
        public virtual void OnApplicationQuit() { }
    }

    // Can be used instead of or in addition to the mod config json
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ModInfoAttribute : Attribute
    {
        public string Id { get; }
        public string Name { get; }
        public string Version { get; }
        public string Author { get; set; }
        public string Description { get; set; }

        public ModInfoAttribute(string id, string name = null, string version = null)
        {
            Id = id;
            Name = name;
            Version = version;
        }
    }

    public sealed class ModInfo
    {
        public string Id { get; }
        public string Name { get; }
        public Version Version { get; }
        public string VersionString { get; }
        public string Author { get; }
        public string Description { get; }
        public string Directory { get; }
        public string AssemblyPath { get; }
        public ModManifest Manifest { get; }

        internal ModInfo(ModManifest manifest, string directory, string assemblyPath)
        {
            Manifest = manifest;
            Id = manifest.Id;
            Name = string.IsNullOrEmpty(manifest.Name) ? manifest.Id : manifest.Name;
            VersionString = string.IsNullOrEmpty(manifest.Version) ? "0.0" : manifest.Version;
            Version = VersionUtil.ParseOrDefault(VersionString);
            Author = manifest.Author ?? "";
            Description = manifest.Description ?? "";
            Directory = directory;
            AssemblyPath = assemblyPath;
        }

        public override string ToString() { return Name + " " + VersionString + " (" + Id + ")"; }
    }

    public enum ModStatus
    {
        Discovered,
        Disabled,
        // A dependency is missing or incompatible
        Skipped,
        Loaded,
        Failed,
    }

    public sealed partial class ModContainer
    {
        public ModInfo Info { get; internal set; }
        public ModStatus Status { get; internal set; }
        // null when mod is not loaded
        public Mod Instance { get; internal set; }
        public Assembly Assembly { get; internal set; }
        public Type EntryType { get; internal set; }
        public string Error { get; internal set; }
        public Exception Exception { get; internal set; }
        public int PatchedMethodCount { get; internal set; }
        public double InitializeMilliseconds { get; internal set; }

        internal bool CallbacksEnabled { get { return Status == ModStatus.Loaded && Instance != null; } }

        public override string ToString() { return (Info != null ? Info.ToString() : "?") + " [" + Status + "]"; }
    }
}
