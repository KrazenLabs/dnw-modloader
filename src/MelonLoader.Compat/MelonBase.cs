using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Semver;

namespace MelonLoader
{
    public abstract class MelonBase
    {
        private static readonly List<MelonBase> AllMelons = new List<MelonBase>();

        protected MelonBase() { }

        public MelonInfoAttribute Info { get; internal set; }
        public MelonGameAttribute[] Games { get; internal set; }
        public MelonProcessAttribute[] SupportedProcesses { get; internal set; }
        public MelonGameVersionAttribute[] SupportedGameVersions { get; internal set; }
        public MelonPlatformAttribute.CompatiblePlatforms[] SupportedPlatforms { get; internal set; }
        public MelonPlatformDomainAttribute.CompatibleDomains SupportedDomain { get; internal set; }
        public SemVersion SupportedMLVersion { get; internal set; }
        public string SupportedMLBuild { get; internal set; }
        public MelonOptionalDependenciesAttribute OptionalDependencies { get; internal set; }
        public MelonAdditionalCreditsAttribute AdditionalCredits { get; internal set; }
        public string ID { get; internal set; }
        public int Priority { get; internal set; }
        public ConsoleColor ConsoleColor { get; internal set; }
        public ConsoleColor AuthorConsoleColor { get; internal set; }
        public Color MelonColor { get; internal set; }
        public Color AuthorColor { get; internal set; }

        public MelonAssembly MelonAssembly { get; internal set; }
        public Assembly Assembly { get { return MelonAssembly != null ? MelonAssembly.Assembly : null; } }
        public string Location { get { return MelonAssembly != null ? MelonAssembly.Location : null; } }
        public string Hash { get { return MelonAssembly != null ? MelonAssembly.Hash : null; } }

        public Harmony HarmonyInstance { get; internal set; }
        public Harmony Harmony { get { return HarmonyInstance; } }
        public Harmony harmonyInstance { get { return HarmonyInstance; } }
        public bool HarmonyDontPatchAll { get; internal set; }

        public MelonLogger.Instance LoggerInstance { get; internal set; }
        public bool Registered { get; internal set; }
        public virtual string MelonTypeName { get { return "Melon"; } }

        public static ReadOnlyCollection<MelonBase> RegisteredMelons { get { lock (AllMelons) return AllMelons.ToList().AsReadOnly(); } }

        public virtual void OnPreSupportModule() { }
        public virtual void OnEarlyInitializeMelon() { }
        public virtual void OnInitializeMelon() { }
        public virtual void OnLateInitializeMelon() { }
        public virtual void OnDeinitializeMelon() { }
        public virtual void OnUpdate() { }
        public virtual void OnFixedUpdate() { }
        public virtual void OnLateUpdate() { }
        public virtual void OnGUI() { }
        public virtual void OnApplicationQuit() { }
        public virtual void OnPreferencesLoaded() { }
        public virtual void OnPreferencesLoaded(string filepath) { }
        public virtual void OnPreferencesSaved() { }
        public virtual void OnPreferencesSaved(string filepath) { }

        // Pre-0.5 spellings. MelonLoader still calls these, so mods written against old docs keep working
        public virtual void OnApplicationStart() { }
        public virtual void OnApplicationLateStart() { }
        public virtual void OnModSettingsApplied() { }

        internal virtual void MarkRegistered()
        {
            if (Registered) return;
            Registered = true;
            lock (AllMelons) AllMelons.Add(this);
        }

        internal virtual void MarkUnregistered() { }

        internal static Action<MelonBase, string> UnregisterHook;

        public bool Register()
        {
            MarkRegistered();
            return true;
        }

        public void Unregister(string reason = null, bool silent = false)
        {
            if (!Registered) return;
            if (MelonAssembly != null) MelonAssembly.UnregisterMelons(reason, silent);
            else UnregisterInstance(reason, silent, true, true);
        }

        internal void UnregisterInstance(string reason, bool silent, bool deinitialize, bool unpatch)
        {
            if (!Registered) return;
            if (deinitialize)
            {
                try { OnDeinitializeMelon(); }
                catch (Exception e) { MelonLogger.Error("OnDeinitializeMelon of " + (Info != null ? Info.Name : GetType().FullName) + " threw: " + e); }
            }
            lock (AllMelons) AllMelons.Remove(this);
            MarkUnregistered();
            if (unpatch && HarmonyInstance != null)
            {
                try { HarmonyInstance.UnpatchSelf(); }
                catch (Exception e) { MelonLogger.Error("Removing Harmony patches of " + (Info != null ? Info.Name : GetType().FullName) + " failed: " + e); }
            }
            Registered = false;
            if (!silent && LoggerInstance != null)
                LoggerInstance.Warning("Unregistered" + (string.IsNullOrEmpty(reason) ? "." : ": " + reason));
            var hook = UnregisterHook;
            if (hook != null) hook(this, reason);
        }

        public static MelonBase FindMelon(string melonName, string melonAuthor)
        {
            lock (AllMelons)
            {
                return AllMelons.FirstOrDefault(m => m.Info != null
                    && string.Equals(m.Info.Name, melonName, StringComparison.Ordinal)
                    && string.Equals(m.Info.Author, melonAuthor, StringComparison.Ordinal));
            }
        }

        // MelonLoader's cross-melon message channel
        public object SendMessage(string name, params object[] arguments)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var method = GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (method == null) return null;
            try { return method.Invoke(this, arguments); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }

        public static List<object> SendMessageAll(string name, params object[] arguments)
        {
            var results = new List<object>();
            foreach (var melon in RegisteredMelons)
            {
                try { results.Add(melon.SendMessage(name, arguments)); }
                catch (Exception e) { MelonLogger.Error("SendMessageAll(" + name + ") threw in " + melon.MelonTypeName + ": " + e.Message); }
            }
            return results;
        }

        public static void ExecuteAll(LemonAction<MelonBase> func, bool unregisterOnFail = false, string unregistrationReason = null)
        {
            ExecuteList(func, RegisteredMelons.ToList(), unregisterOnFail, unregistrationReason);
        }

        public static void ExecuteList(LemonAction<MelonBase> func, List<MelonBase> melons, bool unregisterOnFail = false, string unregistrationReason = null)
        {
            if (func == null || melons == null) return;
            foreach (var melon in melons.ToArray())
            {
                if (melon == null || !melon.Registered) continue;
                try { func(melon); }
                catch (Exception e)
                {
                    if (melon.LoggerInstance != null) melon.LoggerInstance.Error(e.ToString());
                    else MelonLogger.Error(e.ToString());
                    if (unregisterOnFail) melon.Unregister(unregistrationReason);
                }
            }
        }
    }

    // MelonLoader splits melons into kinds so each kind keeps its own registry
    public abstract class MelonTypeBase<T> : MelonBase where T : MelonTypeBase<T>
    {
        private static readonly List<T> Registry = new List<T>();

        protected MelonTypeBase() { }

        public static new ReadOnlyCollection<T> RegisteredMelons { get { lock (Registry) return Registry.ToList().AsReadOnly(); } }

        internal override void MarkRegistered()
        {
            base.MarkRegistered();
            Add((T)this);
        }

        internal override void MarkUnregistered()
        {
            Remove((T)this);
        }

        internal static void Add(T melon)
        {
            lock (Registry)
            {
                if (!Registry.Contains(melon)) Registry.Add(melon);
            }
        }

        internal static void Remove(T melon)
        {
            lock (Registry) Registry.Remove(melon);
        }
    }

    public abstract class MelonMod : MelonTypeBase<MelonMod>
    {
        protected MelonMod() { }

        public override string MelonTypeName { get { return "Mod"; } }

        public MelonInfoAttribute InfoAttribute { get { return Info; } }
        public MelonGameAttribute[] GameAttributes { get { return Games; } }

        public virtual void OnSceneWasLoaded(int buildIndex, string sceneName) { }
        public virtual void OnSceneWasInitialized(int buildIndex, string sceneName) { }
        public virtual void OnSceneWasUnloaded(int buildIndex, string sceneName) { }

        // Pre-0.5 spellings
        public virtual void OnLevelWasLoaded(int level) { }
        public virtual void OnLevelWasInitialized(int level) { }
    }

    public abstract class MelonPlugin : MelonTypeBase<MelonPlugin>
    {
        protected MelonPlugin() { }

        public override string MelonTypeName { get { return "Plugin"; } }

        public MelonInfoAttribute InfoAttribute { get { return Info; } }
        public MelonGameAttribute[] GameAttributes { get { return Games; } }

        public virtual void OnPreModsLoaded() { }
        public virtual void OnPreInitialization() { }
        public virtual void OnApplicationEarlyStart() { }
    }
}
