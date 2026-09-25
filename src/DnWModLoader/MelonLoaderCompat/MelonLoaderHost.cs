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

        private const string SceneInitializedCallback = nameof(MelonMod.OnSceneWasInitialized);

        private static readonly List<string> Files = new List<string>();
        private static readonly List<MelonModAdapter> Adapters = new List<MelonModAdapter>();
        private static readonly List<PendingScene> PendingScenes = new List<PendingScene>();
        private static bool _discovered;
        private static bool _started;
        private static bool _lateStarted;
        private static bool _quitting;

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

            foreach (var file in Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try { LoadFile(file); }
                catch (Exception e) { Log(LoaderLogLevel.Error, "Could not load " + Path.GetFileName(file) + ": " + ModLogger.Brief(e)); }
            }

            var ordered = Adapters.OrderBy(a => a.Priority).ToList();
            Adapters.Clear();
            Adapters.AddRange(ordered);
            foreach (var adapter in Adapters) ModLoader.AddExternalMod(adapter.Container);

            foreach (var adapter in Adapters) Register(adapter);
            Raise(MelonEvents.OnPreSupportModule);
            foreach (var adapter in Adapters) Patch(adapter);
            foreach (var adapter in Adapters) Initialize(adapter);

            // Global MelonEvents
            HostHooks.Add(new Hooks());

            foreach (var adapter in Adapters) Summarize(adapter.Container);

            Raise(MelonEvents.OnApplicationStart);

            ModLoader.Logger.Debug("MelonLoader mods started during " + phase + " in " + stopwatch.Elapsed.TotalMilliseconds.ToString("0") + " ms.");
            if (ModLoader.Phase >= LoaderPhase.Running) LateStart();
        }

        private static string MelonApiVersion { get { return typeof(MelonMod).Assembly.GetName().Version.ToString(3); } }

        private static void SetUpEnvironment()
        {
            MelonLogger.Sink = WriteMelonLog;
            MelonBase.UnregisterHook = OnMelonUnregistered;

            MelonUtils.GameDirectoryValue = ModLoader.GameDirectory;
            MelonUtils.BaseDirectoryValue = ModLoader.GameDirectory;
            MelonUtils.MelonLoaderDirectoryValue = ModLoader.LoaderDirectory;
            MelonUtils.UserDataDirectoryValue = Path.Combine(ModLoader.GameDirectory, "UserData");
            MelonUtils.UserLibsDirectoryValue = Path.Combine(ModLoader.GameDirectory, "UserLibs");

            MelonLoader.Utils.MelonEnvironment.GameRoot = ModLoader.GameDirectory;
            MelonLoader.Utils.MelonEnvironment.LoaderDirectory = ModLoader.LoaderDirectory;

            MelonPreferences.DefaultFilePath = Path.Combine(MelonUtils.UserDataDirectoryValue, "MelonPreferences.cfg");
            UnityPreferenceMappers.Register();

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

            MelonInfoAttribute info;
            try
            {
                info = assembly.GetCustomAttributes(typeof(MelonInfoAttribute), false).OfType<MelonInfoAttribute>().FirstOrDefault();
            }
            catch (Exception e)
            {
                Log(LoaderLogLevel.Warning, "Could not read MelonInfo assembly of " + Path.GetFileName(path) + ": " + ModLogger.Brief(e));
                return;
            }

            if (info == null)
            {
                Log(LoaderLogLevel.Warning, Path.GetFileName(path) + " references MelonLoader but has no MelonInfo assembly.");
                return;
            }

            if (info.SystemType == null)
            {
                Log(LoaderLogLevel.Warning, info.Name + " names no melon type in MelonInfo assembly.");
                return;
            }

            if (typeof(MelonPlugin).IsAssignableFrom(info.SystemType))
            {
                Log(LoaderLogLevel.Warning, info.Name + " is a MelonLoader plugin, which are not yet supported. "
                                            + "Please report this.");
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

            var container = CreateContainer(info, path);
            var adapter = new MelonModAdapter { Container = container, Assembly = assembly, Priority = ReadPriority(assembly) };

            if (ModLoader.Config != null && ModLoader.Config.IsDisabled(container.Info.Id))
            {
                container.Status = ModStatus.Disabled;
                container.Error = "disabled in " + LoaderConfig.FileName;
            }

            string owner;
            if (!ModLoader.ClaimModId(container, out owner) && container.Status == ModStatus.Discovered)
            {
                container.Status = ModStatus.Failed;
                container.Error = "Duplicate mod id; already provided by " + owner;
                Log(LoaderLogLevel.Error, info.Name + ": id " + container.Info.Id + " is already used by " + owner + ".");
            }

            Adapters.Add(adapter);
            if (container.Status != ModStatus.Discovered) return;

            var melonAssembly = new MelonAssembly(assembly, path);
            melonAssembly.HarmonyDontPatchAll = MelonUtils.PullAttributeFromAssembly<HarmonyDontPatchAllAttribute>(assembly) != null;

            MelonMod melon;
            try
            {
                melon = (MelonMod)Activator.CreateInstance(info.SystemType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, null, null);
            }
            catch (Exception e)
            {
                melonAssembly.AddRotten(new RottenMelon(info.SystemType, "Failed to create an instance of the Melon.", e));
                container.Status = ModStatus.Failed;
                container.Exception = e;
                container.Error = "could not be constructed: " + ModLogger.Brief(e);
                Log(LoaderLogLevel.Error, info.Name + " could not be constructed: " + ModLogger.Brief(e));
                return;
            }

            melon.Info = info;
            melon.Games = games;
            melon.MelonAssembly = melonAssembly;
            melon.ID = info.Name;
            melon.Priority = adapter.Priority;
            melon.OptionalDependencies = ReadOptionalDependencies(assembly);
            melon.AdditionalCredits = ReadCredits(assembly);
            melon.HarmonyDontPatchAll = melonAssembly.HarmonyDontPatchAll || TypeSaysDontPatchAll(info.SystemType);
            melon.LoggerInstance = new MelonLogger.Instance(info.Name);
            melonAssembly.AddMelon(melon);
            adapter.Melon = melon;
        }

        private static bool TypeSaysDontPatchAll(Type type)
        {
            try
            {
                return type.GetCustomAttributes(typeof(HarmonyDontPatchAllAttribute), false).Length > 0;
            }
            catch (Exception e)
            {
                ModLoader.Logger.Debug("Could not read the attributes of " + type.FullName + ": " + ModLogger.Brief(e));
                return false;
            }
        }

        private static void Register(MelonModAdapter adapter)
        {
            var container = adapter.Container;
            if (container.Status != ModStatus.Discovered) return;

            var stopwatch = Stopwatch.StartNew();
            var melon = adapter.Melon;
            try
            {
                melon.HarmonyInstance = new Harmony(container.Info.Id);
                container.Instance = adapter;
                adapter.Info = container.Info;
                adapter.Logger = new ModLogger(container.Info.Id);

                melon.MarkRegistered();
                melon.OnEarlyInitializeMelon();
                if (melon.Registered) melon.OnPreSupportModule();
            }
            catch (Exception e)
            {
                Fail(adapter, e);
            }
            finally
            {
                container.InitializeMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        private static void Patch(MelonModAdapter adapter)
        {
            var container = adapter.Container;
            var melon = adapter.Melon;
            if (container.Status != ModStatus.Discovered || !melon.Registered || melon.HarmonyDontPatchAll) return;

            var stopwatch = Stopwatch.StartNew();
            foreach (var type in MelonUtils.GetValidTypes(adapter.Assembly))
            {
                try
                {
                    melon.HarmonyInstance.CreateClassProcessor(type, false).Patch();
                }
                catch (HarmonyException e)
                {
                    ModLoader.Logger.Exception(e, "MelonLoader mod " + container.Info.Id + ": Harmony patches in " + type.FullName + " failed");
                    if (string.IsNullOrEmpty(container.Error)) container.Error = "Harmony patches in " + type.FullName + " failed: " + ModLogger.Brief(e);
                }
                catch (Exception e)
                {
                    ModLoader.Logger.Warning("MelonLoader mod " + container.Info.Id + ": could not check " + type.FullName + " for Harmony patches: " + ModLogger.Brief(e));
                }
            }
            container.InitializeMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
        }

        private static void Initialize(MelonModAdapter adapter)
        {
            var container = adapter.Container;
            if (container.Status != ModStatus.Discovered) return;

            var stopwatch = Stopwatch.StartNew();
            var melon = adapter.Melon;
            try
            {
                // Pre-0.5 entry point
                melon.OnApplicationStart();
                if (melon.Registered) melon.OnInitializeMelon();
                if (!melon.Registered) return;

                container.PatchedMethodCount = melon.HarmonyInstance.GetPatchedMethods().Count();
                container.Status = ModStatus.Loaded;
                SubscribePreferenceCallbacks(adapter);
            }
            catch (Exception e)
            {
                Fail(adapter, e);
            }
            finally
            {
                container.InitializeMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        private static void Fail(MelonModAdapter adapter, Exception e)
        {
            var container = adapter.Container;
            container.Status = ModStatus.Failed;
            container.Exception = e;
            container.Error = e.GetType().Name + ": " + e.Message;
            ModLoader.Logger.Exception(e, "MelonLoader mod " + container.Info.Id + " failed to initialize");
            try { adapter.Melon.MelonAssembly.UnregisterMelons(null, true, false, true); }
            catch (Exception unregisterError) { ModLoader.Logger.Debug("Unregistering " + container.Info.Id + " failed: " + unregisterError.Message); }
        }

        private static void LateStart()
        {
            if (_lateStarted) return;
            _lateStarted = true;
            foreach (var adapter in Adapters)
            {
                var melon = adapter.Melon;
                if (adapter.Active) Safe(adapter, nameof(MelonBase.OnApplicationLateStart), () => melon.OnApplicationLateStart());
                if (adapter.Active) Safe(adapter, nameof(MelonBase.OnLateInitializeMelon), () => melon.OnLateInitializeMelon());
            }
            Raise(MelonEvents.OnApplicationLateStart);
        }

        private static void OnMelonUnregistered(MelonBase melon, string reason)
        {
            if (_quitting) return;
            var adapter = Adapters.FirstOrDefault(a => ReferenceEquals(a.Melon, melon));
            if (adapter == null) return;
            var container = adapter.Container;
            if (container.Status != ModStatus.Loaded && container.Status != ModStatus.Discovered) return;
            container.Status = ModStatus.Skipped;
            container.Error = "Unregistered" + (string.IsNullOrEmpty(reason) ? "" : ": " + reason);
            container.PatchedMethodCount = 0;
        }

        private static void SubscribePreferenceCallbacks(MelonModAdapter adapter)
        {
            var melon = adapter.Melon;
            MelonPreferences.OnPreferencesSaved.Subscribe(path =>
            {
                if (!adapter.Active) return;
                Safe(adapter, nameof(MelonBase.OnPreferencesSaved), () =>
                {
                    melon.OnPreferencesSaved(path);
                    melon.OnPreferencesSaved();
                    melon.OnModSettingsApplied();
                });
            }, melon.Priority);
            MelonPreferences.OnPreferencesLoaded.Subscribe(path =>
            {
                if (!adapter.Active) return;
                Safe(adapter, nameof(MelonBase.OnPreferencesLoaded), () =>
                {
                    melon.OnPreferencesLoaded(path);
                    melon.OnPreferencesLoaded();
                });
            }, melon.Priority);
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

        private static ModContainer CreateContainer(MelonInfoAttribute info, string path)
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
                                          + container.InitializeMilliseconds.ToString("0") + " ms)" + (container.Error != null ? ": " + container.Error : ""));
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

        private static void InitializeScenes()
        {
            if (PendingScenes.Count == 0) return;
            var ready = PendingScenes.FindAll(s => s.Seen);
            PendingScenes.RemoveAll(s => s.Seen);
            foreach (var scene in PendingScenes) scene.Seen = true;

            foreach (var scene in ready)
            {
                foreach (var adapter in Adapters)
                {
                    if (!adapter.Active || adapter.Container.IsCallbackDisabled(SceneInitializedCallback)) continue;
                    try
                    {
                        adapter.Melon.OnSceneWasInitialized(scene.BuildIndex, scene.Name);
                        adapter.Melon.OnLevelWasInitialized(scene.BuildIndex);
                    }
                    catch (Exception e)
                    {
                        adapter.Container.RecordFailure(SceneInitializedCallback, e);
                    }
                }
                MelonEvents.OnSceneWasInitialized.Invoke(scene.BuildIndex, scene.Name);
            }
        }

        internal static void Quit()
        {
            if (!_started) return;
            _quitting = true;
            Raise(MelonEvents.OnApplicationQuit);
            foreach (var adapter in Adapters)
            {
                if (!adapter.Active) continue;
                try { adapter.Melon.MelonAssembly.UnregisterMelons("MelonLoader is deinitializing.", true, true, false); }
                catch (Exception e) { adapter.Container.RecordFailure(nameof(MelonBase.OnDeinitializeMelon), e); }
            }
            Raise(MelonEvents.OnApplicationDefiniteQuit);
            try { MelonPreferences.Save(); }
            catch (Exception e) { ModLoader.Logger.Exception(e, "Saving MelonPreferences failed"); }
        }

        private static void Safe(MelonModAdapter adapter, string callback, Action action)
        {
            try { action(); }
            catch (Exception e) { adapter.Container.RecordFailure(callback, e); }
        }

        private sealed class PendingScene
        {
            public int BuildIndex;
            public string Name;
            public bool Seen;
        }

        private sealed class Hooks : HostHooks
        {
            public override string Name { get { return Framework; } }

            public override void GameStarted() { LateStart(); }
            public override void EarlyUpdate() { InitializeScenes(); }
            public override void Update() { MelonEvents.OnUpdate.Invoke(); }
            public override void FixedUpdate() { MelonEvents.OnFixedUpdate.Invoke(); }
            public override void LateUpdate() { MelonEvents.OnLateUpdate.Invoke(); }
            public override void OnGUI() { MelonEvents.OnGUI.Invoke(); }

            public override void SceneLoaded(Scene scene, LoadSceneMode mode)
            {
                PendingScenes.Add(new PendingScene { BuildIndex = scene.buildIndex, Name = scene.name });
                MelonEvents.OnSceneWasLoaded.Invoke(scene.buildIndex, scene.name);
            }

            public override void SceneUnloaded(Scene scene)
            {
                MelonEvents.OnSceneWasUnloaded.Invoke(scene.buildIndex, scene.name);
            }
        }
    }

    // Presents a MelonMod to the rest of the loader as an ordinary mod
    internal sealed class MelonModAdapter : Mod
    {
        public MelonMod Melon { get; set; }
        public ModContainer Container { get; set; }
        public Assembly Assembly { get; set; }

        public bool Active { get { return Melon != null && Melon.Registered && Container != null && Container.Status == ModStatus.Loaded; } }

        // See MelonLoaderHost.Start
        public int Priority { get; set; }

        // Uses melon's own Harmony instance during init
        public override bool AutoPatch { get { return false; } }

        public override void OnUpdate()
        {
            if (Melon.Registered) Melon.OnUpdate();
        }

        public override void OnFixedUpdate()
        {
            if (Melon.Registered) Melon.OnFixedUpdate();
        }

        public override void OnLateUpdate()
        {
            if (Melon.Registered) Melon.OnLateUpdate();
        }

        public override void OnGUI()
        {
            if (Melon.Registered) Melon.OnGUI();
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Melon.Registered) return;
            Melon.OnSceneWasLoaded(scene.buildIndex, scene.name);
            Melon.OnLevelWasLoaded(scene.buildIndex);
        }

        public override void OnSceneUnloaded(Scene scene)
        {
            if (Melon.Registered) Melon.OnSceneWasUnloaded(scene.buildIndex, scene.name);
        }

        public override void OnApplicationQuit()
        {
            if (Melon.Registered) Melon.OnApplicationQuit();
        }
    }
}
