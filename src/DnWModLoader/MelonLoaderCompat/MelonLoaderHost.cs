using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using DnWModLoader.Logging;
using LoaderLogLevel = DnWModLoader.Logging.LogLevel;

namespace DnWModLoader.MelonLoaderCompat
{
    internal static class MelonLoaderHost
    {
        public const string Framework = "MelonLoader";

        private static readonly List<string> Files = new List<string>();
        private static readonly List<MelonModAdapter> Adapters = new List<MelonModAdapter>();
        private static bool _discovered;
        private static bool _started;

        public static bool HasMelons { get { return Files.Count > 0; } }

        public static void Discover(IList<string> melonFiles)
        {
            if (_discovered) return;
            _discovered = true;
            if (melonFiles != null) Files.AddRange(melonFiles);
        }

        public static void Start(string phase)
        {
            if (_started || Files.Count == 0) return;
            _started = true;

            var stopwatch = Stopwatch.StartNew();
            SetUpEnvironment();
            Log(LoaderLogLevel.Info, "MelonLoader mod support by DnW Mod Loader " + ModLoader.Version + " (MelonLoader API " + MelonApiVersion + ")");

            foreach (var file in Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) LoadFile(file);

            var ordered = Adapters.OrderBy(a => a.Melon.Priority).ToList();
            foreach (var adapter in ordered) Initialize(adapter);

            ModLoader.RefreshLoadedCache();

            // Global MelonEvents
            var pump = ordered.FirstOrDefault(a => a.Active);
            if (pump != null) pump.IsEventPump = true;

            foreach (var adapter in ordered) Summarize(adapter.Container);

            Raise(MelonEvents.OnApplicationStart);
            Raise(MelonEvents.OnApplicationLateStart);
            foreach (var adapter in ordered) Safe(adapter, "OnLateInitializeMelon", () => adapter.Melon.OnLateInitializeMelon());

            ModLoader.Logger.Debug("MelonLoader mods started during " + phase + " in " + stopwatch.Elapsed.TotalMilliseconds.ToString("0") + " ms.");
        }

        private const string MelonApiVersion = "0.7.3";

        private static void SetUpEnvironment()
        {
            MelonLogger.Sink = WriteMelonLog;

            MelonUtils.GameDirectoryValue = ModLoader.GameDirectory;
            MelonUtils.BaseDirectoryValue = ModLoader.GameDirectory;
            MelonUtils.MelonLoaderDirectoryValue = ModLoader.LoaderDirectory;
            MelonUtils.UserDataDirectoryValue = Path.Combine(ModLoader.GameDirectory, "UserData");
            MelonUtils.UserLibsDirectoryValue = Path.Combine(ModLoader.GameDirectory, "UserLibs");

            MelonLoader.Utils.MelonEnvironment.GameRoot = ModLoader.GameDirectory;
            MelonLoader.Utils.MelonEnvironment.LoaderDirectory = ModLoader.LoaderDirectory;

            MelonPreferences.DefaultFilePath = Path.Combine(MelonUtils.UserDataDirectoryValue, "MelonPreferences.cfg");

            MelonCoroutines.Starter = StartCoroutine;
            MelonCoroutines.Stopper = StopCoroutine;

            var userLibs = MelonUtils.UserLibsDirectoryValue;
            if (Directory.Exists(userLibs)) ModLoader.AddResolveDirectory(userLibs);
        }

        private static void WriteMelonLog(string section, string text, MelonLogger.Level level)
        {
            var mapped = level == MelonLogger.Level.Error ? LoaderLogLevel.Error
                : level == MelonLogger.Level.Warning ? LoaderLogLevel.Warning
                : LoaderLogLevel.Info;
            Log(mapped, text, section);
        }

        private static void Log(LoaderLogLevel level, string message, string section = null)
        {
            Logging.Log.Write(level, string.IsNullOrEmpty(section) ? Framework : section, message);
        }

        private static object StartCoroutine(IEnumerator routine)
        {
            var behaviour = ModLoader.Behaviour;
            return behaviour != null ? behaviour.StartCoroutine(routine) : null;
        }

        private static void StopCoroutine(object token)
        {
            var behaviour = ModLoader.Behaviour;
            var coroutine = token as Coroutine;
            if (behaviour != null && coroutine != null) behaviour.StopCoroutine(coroutine);
        }

        private static void LoadFile(string path)
        {
            var searchDirectories = new List<string> { ModLoader.LoaderDirectory, ModLoader.ManagedDirectory, Path.GetDirectoryName(path), MelonUtils.UserLibsDirectoryValue };
            foreach (var missing in ReferenceScan.Scan(path, searchDirectories))
                Log(LoaderLogLevel.Warning, Path.GetFileName(path) + " references " + missing + ", which is not implemented. "
                                        + "Please report this together with the name and version of the mod you were using.");

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch (Exception e)
            {
                Log(LoaderLogLevel.Error, "Could not load " + Path.GetFileName(path) + ": " + e.Message);
                return;
            }

            var info = MelonUtils.PullAttributeFromAssembly<MelonInfoAttribute>(assembly);
            if (info == null)
            {
                Log(LoaderLogLevel.Warning, Path.GetFileName(path) + " references MelonLoader but has no [assembly: MelonInfo].");
                return;
            }

            if (info.SystemType == null)
            {
                Log(LoaderLogLevel.Warning, info.Name + " names no melon type in [assembly: MelonInfo].");
                return;
            }

            if (typeof(MelonPlugin).IsAssignableFrom(info.SystemType))
            {
                Log(LoaderLogLevel.Warning, info.Name + " is a MelonLoader plugin, which are not yet supported. "
                                            + "Please report this as compatibility can potentially be added.");
                return;
            }

            if (!typeof(MelonMod).IsAssignableFrom(info.SystemType))
            {
                Log(LoaderLogLevel.Warning, info.Name + " names " + info.SystemType.FullName + ", which does not derive from MelonMod.");
                return;
            }

            var games = MelonUtils.PullAttributesFromAssembly<MelonGameAttribute>(assembly);
            if (!IsForThisGame(games))
            {
                Log(LoaderLogLevel.Warning, info.Name + " declares it is for a different game (" + DescribeGames(games) + ").");
                return;
            }

            MelonMod melon;
            try
            {
                melon = (MelonMod)Activator.CreateInstance(info.SystemType);
            }
            catch (Exception e)
            {
                Log(LoaderLogLevel.Error, info.Name + " could not be constructed: " + ModLogger.Brief(e));
                return;
            }

            var melonAssembly = MelonAssemblyFactory.Create(assembly, path);
            var adapter = new MelonModAdapter(melon);
            var container = CreateContainer(info, path, adapter);

            melon.Info = info;
            melon.Games = games;
            melon.MelonAssembly = melonAssembly;
            melon.ID = info.Name;
            melon.Priority = ReadPriority(assembly);
            melon.OptionalDependencies = ReadOptionalDependencies(assembly);
            melon.AdditionalCredits = ReadCredits(assembly);
            melon.HarmonyDontPatchAll = assembly.GetCustomAttributes(typeof(HarmonyDontPatchAllAttribute), false).Length > 0
                                        || info.SystemType.GetCustomAttributes(typeof(HarmonyDontPatchAllAttribute), false).Length > 0;
            melon.LoggerInstance = new MelonLogger.Instance(info.Name);

            adapter.Container = container;
            adapter.Assembly = assembly;
            Adapters.Add(adapter);

            string owner;
            if (!ModLoader.ClaimModId(container, out owner))
            {
                container.Status = ModStatus.Failed;
                container.Error = "Duplicate mod id; already provided by " + owner;
                Log(LoaderLogLevel.Error, info.Name + ": id " + container.Info.Id + " is already used by " + owner + ".");
            }

            ModLoader.AddExternalMod(container);
        }

        private static void Initialize(MelonModAdapter adapter)
        {
            var container = adapter.Container;
            if (container.Status != ModStatus.Discovered) return;

            if (ModLoader.Config != null && ModLoader.Config.DisabledMods.Contains(container.Info.Id, StringComparer.OrdinalIgnoreCase))
            {
                container.Status = ModStatus.Disabled;
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var melon = adapter.Melon;

            try
            {
                melon.HarmonyInstance = new Harmony(container.Info.Id);
                container.Instance = adapter;
                adapter.Info = container.Info;
                adapter.Logger = new ModLogger(container.Info.Id);

                melon.MarkRegistered();
                melon.OnPreSupportModule();
                melon.OnEarlyInitializeMelon();

                if (!melon.HarmonyDontPatchAll) melon.HarmonyInstance.PatchAll(adapter.Assembly);

                melon.OnInitializeMelon();
                // Pre-0.5 entry point
                melon.OnApplicationStart();

                container.PatchedMethodCount = melon.HarmonyInstance.GetPatchedMethods().Count();
                container.Status = ModStatus.Loaded;
                container.Error = null;
            }
            catch (Exception e)
            {
                container.Status = ModStatus.Failed;
                container.Exception = e;
                container.Error = e.GetType().Name + ": " + e.Message;
                ModLoader.Logger.Exception(e, "MelonLoader mod " + container.Info.Id + " failed to initialize");
                try { if (melon.HarmonyInstance != null) melon.HarmonyInstance.UnpatchSelf(); }
                catch { }
            }
            finally
            {
                container.InitializeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        // -- metadata ------------------------------------------------------------------------

        private static bool IsForThisGame(MelonGameAttribute[] games)
        {
            if (games == null || games.Length == 0) return true;
            string developer = MelonUtils.GameDeveloper;
            string name = MelonUtils.GameName;
            return games.Any(g => g == null || g.Universal || g.IsCompatible(developer, name));
        }

        private static string DescribeGames(MelonGameAttribute[] games)
        {
            if (games == null || games.Length == 0) return "none declared";
            return string.Join(", ", games.Where(g => g != null && !g.Universal).Select(g => g.Developer + " - " + g.Name).ToArray());
        }

        private static int ReadPriority(Assembly assembly)
        {
            var attribute = MelonUtils.PullAttributeFromAssembly<MelonPriorityAttribute>(assembly);
            return attribute != null ? attribute.Priority : 0;
        }

        private static MelonOptionalDependenciesAttribute ReadOptionalDependencies(Assembly assembly)
        {
            return MelonUtils.PullAttributeFromAssembly<MelonOptionalDependenciesAttribute>(assembly);
        }

        private static MelonAdditionalCreditsAttribute ReadCredits(Assembly assembly)
        {
            return MelonUtils.PullAttributeFromAssembly<MelonAdditionalCreditsAttribute>(assembly);
        }

        private static ModContainer CreateContainer(MelonInfoAttribute info, string path, MelonModAdapter adapter)
        {
            var manifest = new ModManifest
            {
                Id = MakeId(info),
                Name = info.Name,
                Version = info.Version,
                Author = info.Author,
                Url = info.DownloadLink,
            };
            return new ModContainer
            {
                Info = new ModInfo(manifest, Path.GetDirectoryName(path), path),
                Status = ModStatus.Discovered,
                Framework = Framework,
            };
        }

        // MelonLoader has no id concept, using author and name instead
        private static string MakeId(MelonInfoAttribute info)
        {
            string author = Slug(info.Author);
            string name = Slug(info.Name);
            if (string.IsNullOrEmpty(name)) name = "melon";
            return string.IsNullOrEmpty(author) ? "melon." + name : author + "." + name;
        }

        private static string Slug(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' ? c : '-').ToArray();
            return new string(chars).Trim('-');
        }

        // -- dispatch ------------------------------------------------------------------------

        private static void Summarize(ModContainer container)
        {
            string line = container.Info + " [" + Framework + "]";
            switch (container.Status)
            {
                case ModStatus.Loaded:
                    ModLoader.Logger.Info("  [OK]       " + line + " (" + container.PatchedMethodCount + " patched method(s), "
                                          + container.InitializeMilliseconds.ToString("0") + " ms)");
                    break;
                case ModStatus.Disabled: ModLoader.Logger.Info("  [DISABLED] " + line); break;
                case ModStatus.Skipped: ModLoader.Logger.Warning("  [SKIPPED]  " + line + ": " + container.Error); break;
                case ModStatus.Failed: ModLoader.Logger.Error("  [FAILED]   " + line + ": " + container.Error); break;
            }
        }

        private static void Raise(MelonEvent melonEvent)
        {
            try { melonEvent.Invoke(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "A MelonLoader event subscriber threw"); }
        }


        internal static void Quit()
        {
            if (!_started) return;
            Raise(MelonEvents.OnApplicationQuit);
            Raise(MelonEvents.OnApplicationDefiniteQuit);
            try { MelonPreferences.Save(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Saving MelonPreferences failed"); }
        }

        private static void Safe(MelonModAdapter adapter, string callback, Action action)
        {
            try { action(); }
            catch (Exception e) { adapter.Container.RecordFailure(callback, e); }
        }
    }

    internal static class MelonAssemblyFactory
    {
        public static MelonAssembly Create(Assembly assembly, string path)
        {
            var ctor = typeof(MelonAssembly).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(Assembly), typeof(string) }, null);
            return ctor != null ? (MelonAssembly)ctor.Invoke(new object[] { assembly, path }) : null;
        }
    }

    // Presents a MelonMod to the rest of the loader as an ordinary mod
    internal sealed class MelonModAdapter : Mod
    {
        public MelonModAdapter(MelonMod melon) { Melon = melon; }

        public MelonMod Melon { get; private set; }
        public ModContainer Container { get; set; }
        public Assembly Assembly { get; set; }

        public bool Active { get { return Container != null && Container.Status == ModStatus.Loaded && Melon.Registered; } }

        // See MelonLoaderHost.Start
        public bool IsEventPump { get; set; }

        // Uses melon's own Harmony instance during init
        public override bool AutoPatch { get { return false; } }

        public override void OnUpdate()
        {
            Melon.OnUpdate();
            if (IsEventPump) MelonEvents.OnUpdate.Invoke();
        }

        public override void OnFixedUpdate()
        {
            Melon.OnFixedUpdate();
            if (IsEventPump) MelonEvents.OnFixedUpdate.Invoke();
        }

        public override void OnLateUpdate()
        {
            Melon.OnLateUpdate();
            if (IsEventPump) MelonEvents.OnLateUpdate.Invoke();
        }

        public override void OnGUI()
        {
            Melon.OnGUI();
            if (IsEventPump) MelonEvents.OnGUI.Invoke();
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Melon.OnSceneWasLoaded(scene.buildIndex, scene.name);
            Melon.OnLevelWasLoaded(scene.buildIndex);
            Melon.OnSceneWasInitialized(scene.buildIndex, scene.name);
            Melon.OnLevelWasInitialized(scene.buildIndex);
            if (IsEventPump)
            {
                MelonEvents.OnSceneWasLoaded.Invoke(scene.buildIndex, scene.name);
                MelonEvents.OnSceneWasInitialized.Invoke(scene.buildIndex, scene.name);
            }
        }

        public override void OnSceneUnloaded(Scene scene)
        {
            Melon.OnSceneWasUnloaded(scene.buildIndex, scene.name);
            if (IsEventPump) MelonEvents.OnSceneWasUnloaded.Invoke(scene.buildIndex, scene.name);
        }

        public override void OnApplicationQuit()
        {
            Melon.OnApplicationQuit();
            Melon.OnDeinitializeMelon();
        }
    }
}
