using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DnWModLoader.Config;
using DnWModLoader.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DnWModLoader
{
    public enum LoaderPhase
    {
        NotStarted,
        Initializing,
        // All mods are initialized
        Initialized,
        Running,
    }

    public static class ModLoader
    {
        public static readonly string Version = ReadVersion();

        public static readonly Version ParsedVersion = VersionUtil.ParseOrDefault(Version);

        private static string ReadVersion()
        {
            try
            {
                var attribute = (AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(typeof(ModLoader).Assembly, typeof(AssemblyFileVersionAttribute));
                if (attribute != null && System.Version.TryParse(attribute.Version, out var file))
                    return file.ToString(file.Revision > 0 ? 4 : file.Build >= 0 ? 3 : 2);
            }
            catch { }
            return "0.0.0";
        }

        public const string ModsFolderName = "Mods";
        public const string ConfigFolderName = "config";
        public const string LogFileName = "ModLoader.log";
        public const string PreviousLogFileName = "ModLoader.prev.log";

        private static readonly List<ModContainer> ModList = new List<ModContainer>();
        private static readonly Dictionary<string, ModContainer> ModsById = new Dictionary<string, ModContainer>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> SetupProblems = new List<string>();
        private static ModContainer[] _loadedCache = new ModContainer[0];
        private static bool _loadedCacheDirty;
        private static bool _sceneEventsHooked;

        public static string GameDirectory { get; private set; }
        public static string DataDirectory { get; private set; }
        public static string ManagedDirectory { get; private set; }
        public static string ModsDirectory { get; private set; }
        public static string ConfigDirectory { get; private set; }
        public static string LogFilePath { get { return Log.FilePath; } }

        public static ModLogger Logger { get; } = new ModLogger("Loader");

        public static LoaderPhase Phase { get; private set; } = LoaderPhase.NotStarted;
        public static bool IsInitialized { get { return Phase >= LoaderPhase.Initialized; } }

        public static LoaderConfig Config { get; private set; }

        // Every discovered mod in load order
        public static IReadOnlyList<ModContainer> Mods { get { return ModList; } }

        public static event Action ModsInitialized;

        internal static ModLoaderBehaviour Behaviour;

        // The loader's MonoBehaviour. Use to start coroutines or for APIs that need a MonoBehaviour
        public static MonoBehaviour Host { get { return Behaviour; } }

        public static void OpenSettings(string modId = null)
        {
            var overlay = Behaviour?.Overlay;
            if (overlay != null) overlay.OpenSettings(modId);
        }

        public static void ShowOverlay(bool visible)
        {
            var overlay = Behaviour?.Overlay;
            if (overlay != null) overlay.Visible = visible;
        }

        public static bool IsOverlayVisible { get { return Behaviour?.Overlay != null && Behaviour.Overlay.Visible; } }

        public static bool TryGetMod(string id, out ModContainer container)
        {
            if (string.IsNullOrEmpty(id)) { container = null; return false; }
            return ModsById.TryGetValue(id, out container);
        }

        public static bool IsModLoaded(string id)
        {
            return TryGetMod(id, out var c) && c.Status == ModStatus.Loaded;
        }

        // Returns the loaded mod instance
        public static T GetMod<T>() where T : Mod
        {
            foreach (var c in LoadedMods)
                if (c.Instance is T typed) return typed;
            return null;
        }

        // Counts mods per status
        public static void CountStatuses(out int loaded, out int failed, out int skipped, out int disabled)
        {
            loaded = failed = skipped = disabled = 0;
            foreach (var c in ModList)
            {
                switch (c.Status)
                {
                    case ModStatus.Loaded: loaded++; break;
                    case ModStatus.Failed: failed++; break;
                    case ModStatus.Skipped: skipped++; break;
                    case ModStatus.Disabled: disabled++; break;
                }
            }
        }

        internal static void Initialize()
        {
            if (Phase != LoaderPhase.NotStarted) return;
            Phase = LoaderPhase.Initializing;
            var stopwatch = Stopwatch.StartNew();

            ResolvePaths();
            CreateDirectory(ModsDirectory);
            CreateDirectory(ConfigDirectory);
            string fallbackDirectory = FallbackDirectory();
            Log.Open(Path.Combine(ModsDirectory, LogFileName), fallbackDirectory != null ? Path.Combine(fallbackDirectory, LogFileName) : null);
            AssemblyResolver.Logger = Logger;
            if (Log.FilePath != null && !Log.IsFallback) RemoveFallbackLogs(fallbackDirectory);

            Config = LoaderConfig.Load(Path.Combine(ModsDirectory, LoaderConfig.FileName), Logger);
            Log.MinimumLevel = Config.LogLevel;
            Log.EchoToUnity = Config.EchoLoaderLogToUnity;

            LogHeader();
            HookUnityLog();
            AssemblyResolver.Install(LoaderDirectory);
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            try { HarmonyLog.Forward(); }
            catch (Exception e) { Logger.Debug("Could not forward HarmonyX's log: " + e.Message); }
            EnsureBehaviour("SubsystemRegistration");

            try
            {
                DiscoverAndLoadMods();
            }
            catch (Exception e)
            {
                Logger.Exception(e, "Mod discovery failed");
            }

            try
            {
                _bepInExPluginsFound = HasBepInExPlugins();
                if (_bepInExPluginsFound) DiscoverBepInExPlugins();
            }
            catch (Exception e)
            {
                Logger.Exception(e, "BepInEx plugin discovery failed");
            }

            try
            {
                WarnAboutMelonPlugins();
                _melonsFound = ModsFolderMelons.Count > 0;
                if (_melonsFound) DiscoverMelons();
            }
            catch (Exception e)
            {
                Logger.Exception(e, "MelonLoader mod discovery failed");
            }

            Phase = LoaderPhase.Initialized;
            CountStatuses(out int loaded, out int failed, out int skipped, out int disabled);
            int waiting = ModList.Count(c => c.Framework != null && c.Status == ModStatus.Discovered);
            Logger.Info("Initialization finished in " + stopwatch.ElapsedMilliseconds + " ms: " + loaded + " loaded, " + failed + " failed, " + skipped + " skipped, " + disabled + " disabled"
                        + (waiting > 0 ? ", " + waiting + " hosted plugin(s)/mod(s) found." : "."));

            RaiseModsInitialized();
            HookSceneEvents();

            if (!Preloader.AfterRegistrationHooked)
            {
                StartBepInExPlugins("SubsystemRegistration");
                StartMelons("SubsystemRegistration");
            }
        }

        internal static void AfterRegistration()
        {
            string phase = Preloader.AfterRegistrationPhase ?? "a later initializer";
            EnsureBehaviour(phase);
            StartBepInExPlugins(phase);
            StartMelons(phase);
        }

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

        private static string HandlerOwner(Delegate handler)
        {
            var type = handler.Method.DeclaringType;
            return type == null ? handler.Method.Name : type.Assembly.GetName().Name + " (" + type.FullName + "." + handler.Method.Name + ")";
        }

        internal static void GameStarted()
        {
            if (Phase >= LoaderPhase.Running) return;
            Phase = LoaderPhase.Running;
            Logger.Info("First scene loaded: " + SafeActiveSceneName() + ". Game is running.");
            Dispatch(nameof(Mod.OnGameStarted), GameStartedAction);
            HostHooks.Run(GameStartedHook);
        }

        internal static void Shutdown()
        {
            Logger.Info("Application quitting.");
            if (_melonsFound)
            {
                try { QuitMelons(); }
                catch (Exception e) { Logger.Exception(e, "Shutting down MelonLoader mods failed"); }
            }
            Log.Close();
        }

        private static readonly Action<Mod> GameStartedAction = m => m.OnGameStarted();
        private static readonly Action<HostHooks> GameStartedHook = h => h.GameStarted();

        private static string SafeActiveSceneName()
        {
            try { return SceneManager.GetActiveScene().name; } catch { return "?"; }
        }

        public static string LoaderDirectory { get; private set; }

        private static void ResolvePaths()
        {
            string loaderDir = null;
            try
            {
                string location = typeof(ModLoader).Assembly.Location;
                if (!string.IsNullOrEmpty(location)) loaderDir = Path.GetDirectoryName(Path.GetFullPath(location));
            }
            catch { }
            LoaderDirectory = loaderDir;

            string data = null;
            try { data = Application.dataPath; } catch { }

            string game = null;
            if (Preloader.Invoked && !string.IsNullOrEmpty(Preloader.GameDirectory)) game = Preloader.GameDirectory;
            else if (!string.IsNullOrEmpty(data)) game = Path.GetDirectoryName(Path.GetFullPath(data));
            else if (!string.IsNullOrEmpty(loaderDir)) game = Preloader.GuessGameDirectory(loaderDir);
            if (string.IsNullOrEmpty(game)) game = Path.GetFullPath(".");

            GameDirectory = Path.GetFullPath(game);
            DataDirectory = !string.IsNullOrEmpty(data) ? Path.GetFullPath(data) : (Path.GetDirectoryName(Preloader.FindManagedDirectory(GameDirectory) ?? Path.Combine(GameDirectory, "Data")) ?? GameDirectory);
            ManagedDirectory = Preloader.ManagedDirectory ?? Preloader.FindManagedDirectory(GameDirectory) ?? Path.Combine(DataDirectory, "Managed");
            ModsDirectory = Path.Combine(GameDirectory, ModsFolderName);
            ConfigDirectory = Path.Combine(ModsDirectory, ConfigFolderName);
        }

        private static void CreateDirectory(string directory)
        {
            try { Directory.CreateDirectory(directory); }
            catch (Exception e) { SetupProblems.Add("Could not create " + directory + ": " + e.Message); }
        }

        internal static string FallbackDirectory()
        {
            try
            {
                string persistent = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(persistent)) return Path.GetFullPath(persistent);
            }
            catch { }
            try { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DnWModLoader"); }
            catch { return null; }
        }

        private static void RemoveFallbackLogs(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            foreach (var name in new[] { LogFileName, PreviousLogFileName })
            {
                try
                {
                    string path = Path.Combine(directory, name);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
        }

        private static void LogHeader()
        {
            Logger.Info("DnW Mod Loader " + Version + " starting (" + Preloader.Status + ")");
            try
            {
                Logger.Info("Game: " + Application.productName + " " + Application.version + " by " + Application.companyName
                            + " | Unity " + Application.unityVersion + " | " + Application.platform);
            }
            catch (Exception e) { Logger.Debug("Application info unavailable: " + e.Message); }
            if (Direct3D12Warning.Applies()) Logger.Warning(Direct3D12Warning.LogMessage);
            if (ParallelLoaderWarning.Applies()) Logger.Error(ParallelLoaderWarning.LogMessage);
            if (WriteProtectionWarning.Applies()) Logger.Warning(WriteProtectionWarning.LogMessage);
            foreach (var problem in SetupProblems) Logger.Warning(problem);
            try { Logger.Debug("OS: " + SystemInfo.operatingSystem + " | CLR: " + Environment.Version + " | 64-bit: " + Environment.Is64BitProcess); } catch { }
            try { Logger.Debug("Command line: " + string.Join(" ", Environment.GetCommandLineArgs())); } catch { }
            foreach (var line in Preloader.TakeEarlyLog()) Logger.Debug("[preloader] " + line);
            Logger.Debug("Game directory: " + GameDirectory);
            Logger.Debug("Loader directory: " + LoaderDirectory);
            Logger.Debug("Mods directory: " + ModsDirectory);
            Logger.Debug("Log file: " + Log.FilePath);
            Logger.Debug("Config: hotkey=" + Config.OverlayHotkey + " logLevel=" + Config.LogLevel + " mirrorUnityLog=" + Config.MirrorUnityLog
                         + " disabledMods=[" + string.Join(", ", Config.DisabledMods) + "]");
        }

        private static void HookUnityLog()
        {
            try { Application.logMessageReceivedThreaded += OnUnityLogMessage; }
            catch (Exception e) { Logger.Debug("Could not hook Unity log: " + e.Message); }
        }

        private static void OnUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            var mirror = Config.MirrorUnityLog;
            if (mirror == UnityLogMirror.None || Log.IsEchoedLine(condition)) return;
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    if (mirror >= UnityLogMirror.Error)
                    {
                        string text = condition;
                        if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace)) text += "\n" + TrimStack(stackTrace, 12);
                        Log.Write(LogLevel.Error, "Unity", text);
                    }
                    break;
                case LogType.Warning:
                    if (mirror >= UnityLogMirror.Warning) Log.Write(LogLevel.Warning, "Unity", condition);
                    break;
                default:
                    if (mirror >= UnityLogMirror.All) Log.Write(LogLevel.Debug, "Unity", condition);
                    break;
            }
        }

        private static string TrimStack(string stack, int maxLines)
        {
            var lines = stack.Replace("\r", "").Split('\n');
            if (lines.Length <= maxLines) return stack.TrimEnd();
            return string.Join("\n", lines.Take(maxLines)) + "\n  ... (" + (lines.Length - maxLines) + " more)";
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { Logger.Error("Unhandled exception (terminating=" + e.IsTerminating + "): " + ModLogger.Describe(e.ExceptionObject as Exception)); }
            catch { }
        }

        // Used for emergency logging
        internal static string GuessGameDirectory()
        {
            if (!string.IsNullOrEmpty(GameDirectory)) return GameDirectory;
            if (!string.IsNullOrEmpty(Preloader.GameDirectory)) return Preloader.GameDirectory;
            try
            {
                string location = typeof(ModLoader).Assembly.Location;
                if (!string.IsNullOrEmpty(location)) return Preloader.GuessGameDirectory(Path.GetDirectoryName(Path.GetFullPath(location)));
            }
            catch { }
            return Path.GetFullPath(".");
        }

        private sealed class Candidate
        {
            public string Directory;
            public ModManifest Manifest;
            public string AssemblyPath;
            public bool IsBare;
            public string DiscoveryError;
        }

        private static HashSet<string> _bundledAssemblies;
        private static string _bundledFrom;

        internal static bool IsBundledAssembly(string assemblyName)
        {
            return !string.IsNullOrEmpty(assemblyName) && BundledAssemblies().Contains(assemblyName);
        }

        private static HashSet<string> BundledAssemblies()
        {
            string directory = LoaderDirectory;
            var bundled = _bundledAssemblies;
            if (bundled != null && string.Equals(_bundledFrom, directory, StringComparison.OrdinalIgnoreCase)) return bundled;

            bundled = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { typeof(ModLoader).Assembly.GetName().Name };
            if (!string.IsNullOrEmpty(directory))
            {
                foreach (var file in SafeGetFiles(directory, "*.dll"))
                    if (string.Equals(Path.GetExtension(file), ".dll", StringComparison.OrdinalIgnoreCase)) bundled.Add(Path.GetFileNameWithoutExtension(file));
            }
            _bundledFrom = directory;
            _bundledAssemblies = bundled;
            return bundled;
        }

        // BepInEx plugins within Mods folder
        private static readonly List<string> ModsFolderBepInExPlugins = new List<string>();
        // BepInEx.dll is only loaded when there are plugins
        private static bool _bepInExPluginsFound;

        private static bool HasBepInExPlugins()
        {
            string plugins = Path.Combine(Path.Combine(GameDirectory, "BepInEx"), "plugins");
            return ModsFolderBepInExPlugins.Count > 0 || (Directory.Exists(plugins) && Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories).Length > 0);
        }

        // In case BepInEx.dll is missing
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DiscoverBepInExPlugins()
        {
            BepInExCompat.BepInExHost.Discover(ModsFolderBepInExPlugins);
        }

        private static void StartBepInExPlugins(string phase)
        {
            if (!_bepInExPluginsFound) return;
            try { StartBepInExPluginsCore(phase); }
            catch (Exception e) { Logger.Exception(e, "Starting BepInEx plugins failed"); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StartBepInExPluginsCore(string phase)
        {
            BepInExCompat.BepInExHost.Start(phase);
        }

        private static readonly List<string> ModsFolderMelons = new List<string>();
        // MelonLoader.dll is only loaded when there are melons
        private static bool _melonsFound;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DiscoverMelons()
        {
            MelonLoaderCompat.MelonLoaderHost.Discover(ModsFolderMelons);
        }

        // Warn about missing MelonLoader plugin support
        private static void WarnAboutMelonPlugins()
        {
            string plugins = Path.Combine(GameDirectory, "Plugins");
            string[] files;
            try { files = Directory.Exists(plugins) ? Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories) : new string[0]; }
            catch (Exception) { return; }
            if (files.Length == 0) return;

            Logger.Warning(files.Length + " file(s) in the Plugins folder: MelonLoader plugins start before the game "
                           + "engine does, which this loader cannot do, so they are not loaded. MelonLoader mods go in "
                           + ModsFolderName + " and do work.");
            foreach (var file in files)
                Logger.Debug("  not loaded: Plugins" + Path.DirectorySeparatorChar + file.Substring(plugins.Length).TrimStart(Path.DirectorySeparatorChar));
        }

        private static void StartMelons(string phase)
        {
            if (!_melonsFound) return;
            try { StartMelonsCore(phase); }
            catch (Exception e) { Logger.Exception(e, "Starting MelonLoader mods failed"); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StartMelonsCore(string phase)
        {
            MelonLoaderCompat.MelonLoaderHost.Start(phase);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void QuitMelons()
        {
            MelonLoaderCompat.MelonLoaderHost.Quit();
        }

        internal static void AddExternalMod(ModContainer container)
        {
            ModList.Add(container);
            InvalidateLoadedCache();
        }

        internal static bool ClaimModId(ModContainer container, out string owner)
        {
            owner = null;
            string id = container.Info?.Id;
            if (string.IsNullOrEmpty(id)) return true;
            if (ModsById.TryGetValue(id, out var existing) && !ReferenceEquals(existing, container) && ClaimRank(container) <= ClaimRank(existing))
            {
                owner = existing.Info.ToString();
                return false;
            }
            ModsById[id] = container;
            return true;
        }

        private static int ClaimRank(ModContainer container)
        {
            switch (container.Status)
            {
                case ModStatus.Discovered:
                case ModStatus.Loaded:
                    return 2;
                case ModStatus.Disabled:
                    return 1;
                default:
                    return 0;
            }
        }

        private enum BareDllKind
        {
            Native,
            DnwMod,
            BepInExPlugin,
            Melon,
            Other,
        }

        private static Mono.Cecil.ModuleDefinition ReadModule(string path)
        {
            return Mono.Cecil.ModuleDefinition.ReadModule(path, new Mono.Cecil.ReaderParameters { ReadingMode = Mono.Cecil.ReadingMode.Deferred });
        }

        private static BareDllKind ClassifyBareDll(string path)
        {
            try
            {
                using (var module = ReadModule(path))
                {
                    if (DefinesModSubclass(module)) return BareDllKind.DnwMod;
                    if (module.AssemblyReferences.Any(r => r.Name == "BepInEx")) return BareDllKind.BepInExPlugin;
                    if (module.AssemblyReferences.Any(r => r.Name == "MelonLoader")) return BareDllKind.Melon;
                    return BareDllKind.Other;
                }
            }
            catch (BadImageFormatException)
            {
                return BareDllKind.Native;
            }
            catch
            {
                return BareDllKind.Other;
            }
        }

        private static bool IsNativeDll(string path)
        {
            try
            {
                using (ReadModule(path)) return false;
            }
            catch (BadImageFormatException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        // First check if a dll contains the DnW Mod subclass
        private static bool DefinesModSubclass(Mono.Cecil.ModuleDefinition module)
        {
            if (!module.AssemblyReferences.Any(r => r.Name == typeof(Mod).Assembly.GetName().Name)) return false;
            foreach (var type in module.GetTypes())
            {
                if (type.IsAbstract) continue;
                var baseType = type.BaseType;
                for (int depth = 0; baseType != null && depth < 32; depth++)
                {
                    if (baseType.FullName == typeof(Mod).FullName) return true;
                    var definition = baseType.GetElementType() as Mono.Cecil.TypeDefinition;
                    if (definition == null) break;
                    baseType = definition.BaseType;
                }
            }
            return false;
        }

        private static void DiscoverAndLoadMods()
        {
            var candidates = Discover();
            Logger.Info("Discovered " + candidates.Count + " mod candidate(s) in " + ModsDirectory);

            AssemblyResolver.AddDirectory(ModsDirectory);
            foreach (var candidate in candidates)
            {
                AssemblyResolver.AddDirectory(candidate.Directory);
                AssemblyResolver.AddDirectory(Path.Combine(candidate.Directory, "lib"));
                AssemblyResolver.AddDirectory(Path.Combine(candidate.Directory, "libs"));
            }

            var containers = new List<ModContainer>();
            foreach (var candidate in candidates)
            {
                ModContainer container;
                try
                {
                    container = LoadCandidate(candidate);
                }
                catch (Exception e)
                {
                    Logger.Exception(e, "Loading " + (candidate.AssemblyPath ?? candidate.Directory) + " failed");
                    container = new ModContainer { Info = FallbackInfo(candidate), Exception = e };
                    Fail(container, "could not be loaded: " + ModLogger.Brief(e));
                }
                if (container != null) containers.Add(container);
            }

            // Handles duplicate ids
            foreach (var container in containers)
            {
                if (ClaimModId(container, out _) || container.Status != ModStatus.Discovered) continue;
                var owner = ModsById[container.Info.Id];
                container.Status = ModStatus.Failed;
                container.Error = "Duplicate mod id; already provided by " + owner.Info.AssemblyPath;
                Logger.Error("Mod id " + container.Info.Id + " is used twice: " + owner.Info.AssemblyPath + " and " + container.Info.AssemblyPath);
            }

            var ordered = OrderForLoading(containers.Where(c => c.Status == ModStatus.Discovered).ToList());

            var rest = containers.Where(c => !ordered.Contains(c)).ToList();
            ModList.Clear();
            ModList.AddRange(ordered);
            ModList.AddRange(rest);
            InvalidateLoadedCache();

            foreach (var container in ordered) InitializeMod(container);
            Dispatch(nameof(Mod.OnAllModsInitialized), m => m.OnAllModsInitialized());

            foreach (var container in ModList)
            {
                string line = container.Info != null ? container.Info.ToString() : Path.GetFileName(container.Assembly?.Location ?? "?");
                switch (container.Status)
                {
                    case ModStatus.Loaded: Logger.Info("  [OK]       " + line + " (" + container.PatchedMethodCount + " patched method(s), " + container.InitializeMilliseconds.ToString("0") + " ms)"); break;
                    case ModStatus.Disabled: Logger.Info("  [DISABLED] " + line); break;
                    case ModStatus.Skipped: Logger.Warning("  [SKIPPED]  " + line + ": " + container.Error); break;
                    case ModStatus.Failed: Logger.Error("  [FAILED]   " + line + ": " + container.Error); break;
                    default: Logger.Warning("  [?]        " + line); break;
                }
            }
        }

        private static List<Candidate> Discover()
        {
            var result = new List<Candidate>();
            if (!Directory.Exists(ModsDirectory)) return result;

            foreach (var dir in Directory.GetDirectories(ModsDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string folderName = Path.GetFileName(dir);
                if (string.Equals(folderName, ConfigFolderName, StringComparison.OrdinalIgnoreCase)) continue;
                if (folderName.StartsWith(".") || folderName.StartsWith("_"))
                {
                    Logger.Debug("Ignoring folder " + folderName + " (name starts with . or _)");
                    continue;
                }

                string manifestPath = Path.Combine(dir, "mod.json");
                if (File.Exists(manifestPath))
                {
                    var candidate = new Candidate { Directory = dir };
                    try
                    {
                        candidate.Manifest = ModManifest.Parse(File.ReadAllText(manifestPath));
                        candidate.AssemblyPath = !string.IsNullOrEmpty(candidate.Manifest.Assembly)
                            ? Path.Combine(dir, candidate.Manifest.Assembly)
                            : PickAssembly(dir, folderName, out candidate.DiscoveryError);
                    }
                    catch (Exception e)
                    {
                        candidate.DiscoveryError = "mod.json could not be parsed: " + e.Message;
                    }
                    result.Add(candidate);
                    continue;
                }

                var dlls = SafeGetFiles(dir, "*.dll");
                if (dlls.Length == 0)
                {
                    Logger.Debug("Ignoring folder " + folderName + " (no mod.json and no DLL)");
                    continue;
                }
                foreach (var dll in dlls.OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) AddBareDll(result, dir, dll);
            }

            foreach (var dll in SafeGetFiles(ModsDirectory, "*.dll").OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) AddBareDll(result, ModsDirectory, dll);
            return result;
        }

        private static void AddBareDll(List<Candidate> result, string directory, string dll)
        {
            if (IsReservedDll(dll)) return;
            switch (ClassifyBareDll(dll))
            {
                case BareDllKind.Native:
                    Logger.Debug("Ignoring " + dll + " (not a .NET assembly, assuming it is a native library)");
                    break;
                case BareDllKind.BepInExPlugin:
                    Logger.Debug(dll + " seems to be a BepInEx plugin");
                    ModsFolderBepInExPlugins.Add(dll);
                    break;
                case BareDllKind.Melon:
                    Logger.Debug(dll + " seems to be a MelonLoader mod");
                    ModsFolderMelons.Add(dll);
                    break;
                default:
                    result.Add(new Candidate { Directory = directory, AssemblyPath = dll, IsBare = true });
                    break;
            }
        }

        private static string ReservedName(string path)
        {
            return IsBundledAssembly(Path.GetFileNameWithoutExtension(path)) ? Path.GetFileName(path) : null;
        }

        private static bool IsReservedDll(string path)
        {
            string reserved = ReservedName(path);
            if (reserved == null) return false;
            Logger.Warning("Ignoring " + path + ": " + reserved + " is a dll reserved by the mod loader");
            return true;
        }

        private static string[] SafeGetFiles(string dir, string pattern)
        {
            try { return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch { return new string[0]; }
        }

        private static string PickAssembly(string dir, string folderName, out string error)
        {
            error = null;
            var files = SafeGetFiles(dir, "*.dll").Where(d => ReservedName(d) == null).ToArray();
            if (files.Length == 0) { error = "the mod folder contains no DLL"; return null; }
            var dlls = files.Where(d => !IsNativeDll(d)).ToArray();
            if (dlls.Length == 0) { error = "the mod folder contains no compatible DLL"; return null; }
            if (dlls.Length == 1) return dlls[0];
            foreach (var dll in dlls)
                if (string.Equals(Path.GetFileNameWithoutExtension(dll), folderName, StringComparison.OrdinalIgnoreCase)) return dll;
            error = "the mod folder contains several DLLs; set \"assembly\" in mod.json";
            return null;
        }

        private static ModContainer LoadCandidate(Candidate candidate)
        {
            var container = new ModContainer { Status = ModStatus.Discovered };

            if (candidate.DiscoveryError != null)
            {
                container.Info = candidate.Manifest != null && !string.IsNullOrEmpty(candidate.Manifest.Id)
                    ? new ModInfo(candidate.Manifest, candidate.Directory, candidate.AssemblyPath)
                    : new ModInfo(new ModManifest { Id = Path.GetFileName(candidate.Directory).ToLowerInvariant(), Name = Path.GetFileName(candidate.Directory) }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, candidate.DiscoveryError);
            }

            // Manifest mods can be disabled without loading their assembly
            if (candidate.Manifest != null)
            {
                var manifest = candidate.Manifest;
                if (!ModManifest.IsValidId(manifest.Id))
                {
                    container.Info = new ModInfo(new ModManifest { Id = Path.GetFileName(candidate.Directory).ToLowerInvariant(), Name = manifest.Name ?? Path.GetFileName(candidate.Directory), Version = manifest.Version }, candidate.Directory, candidate.AssemblyPath);
                    return Fail(container, "mod.json has a missing or invalid \"id\" (use lower-case letters, digits, '.', '_' or '-'): " + (manifest.Id ?? "(null)"));
                }
                if (!manifest.Enabled || Config.IsDisabled(manifest.Id))
                {
                    container.Info = new ModInfo(manifest, candidate.Directory, candidate.AssemblyPath);
                    container.Status = ModStatus.Disabled;
                    container.Error = !manifest.Enabled ? "disabled in mod.json" : "disabled in " + LoaderConfig.FileName;
                    return container;
                }
            }

            if (string.IsNullOrEmpty(candidate.AssemblyPath) || !File.Exists(candidate.AssemblyPath))
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = Path.GetFileName(candidate.Directory).ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, "assembly not found: " + (candidate.AssemblyPath ?? "(none)"));
            }

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(candidate.AssemblyPath);
            }
            catch (Exception e)
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = Path.GetFileNameWithoutExtension(candidate.AssemblyPath).ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                container.Exception = e;
                return Fail(container, "could not load assembly: " + e.GetType().Name + ": " + e.Message);
            }

            string loadedFrom = LoadedFrom(assembly);
            if (!SamePath(loadedFrom, candidate.AssemblyPath))
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = assembly.GetName().Name.ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, "assembly name " + assembly.GetName().Name + " is already used by " + (string.IsNullOrEmpty(loadedFrom) ? "an assembly loaded from memory" : loadedFrom));
            }
            container.Assembly = assembly;

            Type entryType;
            string entryError;
            try
            {
                entryType = FindEntryType(assembly, candidate.Manifest?.EntryType, out entryError);
            }
            catch (Exception e)
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = assembly.GetName().Name.ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                container.Exception = e;
                return Fail(container, "could not inspect assembly: " + ModLogger.Describe(e));
            }

            if (entryType == null)
            {
                if (candidate.IsBare && entryError == null)
                {
                    Logger.Debug("Ignoring " + candidate.AssemblyPath + " (no Mod subclass, treated as a library)");
                    return null;
                }
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = assembly.GetName().Name.ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, entryError ?? "no class deriving from DnWModLoader.Mod found in " + Path.GetFileName(candidate.AssemblyPath));
            }
            container.EntryType = entryType;

            ModInfoAttribute attribute;
            try
            {
                attribute = entryType.GetCustomAttribute<ModInfoAttribute>();
            }
            catch (Exception e)
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = assembly.GetName().Name.ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                container.Exception = e;
                return Fail(container, "could not read the attributes of " + entryType.FullName + ": " + ModLogger.Brief(e));
            }
            var effective = candidate.Manifest ?? new ModManifest();
            if (string.IsNullOrEmpty(effective.Id)) effective.Id = attribute?.Id;
            if (string.IsNullOrEmpty(effective.Id)) effective.Id = SanitizeId(assembly.GetName().Name);
            if (string.IsNullOrEmpty(effective.Name)) effective.Name = attribute?.Name ?? entryType.Name;
            if (string.IsNullOrEmpty(effective.Version)) effective.Version = attribute?.Version ?? assembly.GetName().Version?.ToString() ?? "0.0";
            if (string.IsNullOrEmpty(effective.Author)) effective.Author = attribute?.Author;
            if (string.IsNullOrEmpty(effective.Description)) effective.Description = attribute?.Description;

            if (!ModManifest.IsValidId(effective.Id))
            {
                effective.Id = SanitizeId(effective.Id);
                if (!ModManifest.IsValidId(effective.Id)) effective.Id = SanitizeId(assembly.GetName().Name);
            }

            container.Info = new ModInfo(effective, candidate.Directory, candidate.AssemblyPath);

            if (candidate.IsBare && Config.IsDisabled(container.Info.Id))
            {
                container.Status = ModStatus.Disabled;
                container.Error = "disabled in " + LoaderConfig.FileName;
            }
            return container;
        }

        private static ModContainer Fail(ModContainer container, string error)
        {
            container.Status = ModStatus.Failed;
            container.Error = error;
            return container;
        }

        private static ModInfo FallbackInfo(Candidate candidate)
        {
            var manifest = candidate.Manifest != null && !string.IsNullOrEmpty(candidate.Manifest.Id)
                ? candidate.Manifest
                : new ModManifest { Id = SanitizeId(Path.GetFileNameWithoutExtension(candidate.AssemblyPath ?? candidate.Directory)) };
            return new ModInfo(manifest, candidate.Directory, candidate.AssemblyPath);
        }

        private static string LoadedFrom(Assembly assembly)
        {
            try { return assembly.Location; }
            catch { return null; }
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string SanitizeId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "unnamed";
            var chars = raw.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-' ? ch : '-').ToArray();
            string id = new string(chars).Trim('-', '.', '_');
            return string.IsNullOrEmpty(id) ? "unnamed" : id;
        }

        private static Type FindEntryType(Assembly assembly, string requestedName, out string error)
        {
            error = null;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); Logger.Warning("Some types in " + assembly.GetName().Name + " could not be loaded: " + ModLogger.Describe(e)); }

            if (!string.IsNullOrEmpty(requestedName))
            {
                var requested = types.FirstOrDefault(t => t.FullName == requestedName || t.Name == requestedName);
                if (requested == null) { error = "entry type " + requestedName + " not found in assembly"; return null; }
                if (requested.IsAbstract || !typeof(Mod).IsAssignableFrom(requested)) { error = "entry type " + requestedName + " does not derive from DnWModLoader.Mod"; return null; }
                return requested;
            }

            var entries = types.Where(t => !t.IsAbstract && typeof(Mod).IsAssignableFrom(t)).ToList();
            if (entries.Count == 0) return null;
            if (entries.Count == 1) return entries[0];
            error = "several classes derive from DnWModLoader.Mod (" + string.Join(", ", entries.Select(t => t.FullName)) + "); set \"entryType\" in mod.json";
            return null;
        }

        private static List<ModContainer> OrderForLoading(List<ModContainer> loadable)
        {
            var byId = loadable.ToDictionary(c => c.Info.Id, c => c, StringComparer.OrdinalIgnoreCase);
            var incoming = loadable.ToDictionary(c => c.Info.Id, c => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            var outgoing = loadable.ToDictionary(c => c.Info.Id, c => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

            void Edge(string before, string after)
            {
                if (!byId.ContainsKey(before) || !byId.ContainsKey(after) || string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) return;
                if (outgoing[before].Add(after)) incoming[after].Add(before);
            }

            foreach (var c in loadable)
            {
                var m = c.Info.Manifest;
                foreach (var dep in m.Dependencies) if (dep != null && !string.IsNullOrEmpty(dep.Id)) Edge(dep.Id, c.Info.Id);
                foreach (var id in m.LoadAfter) if (!string.IsNullOrEmpty(id)) Edge(id, c.Info.Id);
                foreach (var id in m.LoadBefore) if (!string.IsNullOrEmpty(id)) Edge(c.Info.Id, id);
            }

            var ready = new SortedSet<string>(loadable.Where(c => incoming[c.Info.Id].Count == 0).Select(c => c.Info.Id), StringComparer.OrdinalIgnoreCase);
            var ordered = new List<ModContainer>();
            while (ready.Count > 0)
            {
                string id = ready.Min;
                ready.Remove(id);
                ordered.Add(byId[id]);
                foreach (var next in outgoing[id])
                {
                    incoming[next].Remove(id);
                    if (incoming[next].Count == 0) ready.Add(next);
                }
            }

            if (ordered.Count != loadable.Count)
            {
                foreach (var c in loadable.Where(c => !ordered.Contains(c)))
                {
                    c.Status = ModStatus.Skipped;
                    c.Error = "dependency / load-order cycle involving " + string.Join(", ", incoming[c.Info.Id]);
                    Logger.Error("Mod " + c.Info.Id + " is part of a load-order cycle and will not be loaded.");
                }
            }
            return ordered;
        }

        private static void InitializeMod(ModContainer container)
        {
            if (container.Status != ModStatus.Discovered) return;
            var info = container.Info;
            var manifest = info.Manifest;

            if (!string.IsNullOrEmpty(manifest.LoaderVersion))
            {
                if (!VersionConstraint.TryParse(manifest.LoaderVersion, out var constraint))
                {
                    Logger.Warning("Mod " + info.Id + " has an unparsable loaderVersion \"" + manifest.LoaderVersion + "\"; ignoring it.");
                }
                else if (!constraint.Satisfies(ParsedVersion))
                {
                    container.Status = ModStatus.Skipped;
                    container.Error = "requires loader " + manifest.LoaderVersion + " but " + Version + " is installed";
                    return;
                }
            }

            foreach (var dep in manifest.Dependencies)
            {
                if (dep == null || string.IsNullOrEmpty(dep.Id)) continue;
                if (!ModsById.TryGetValue(dep.Id, out var target))
                {
                    if (dep.Optional) continue;
                    container.Status = ModStatus.Skipped;
                    container.Error = "missing dependency " + dep;
                    return;
                }
                if (target.Status != ModStatus.Loaded)
                {
                    if (dep.Optional) continue;
                    container.Status = ModStatus.Skipped;
                    container.Error = "dependency " + dep.Id + " is " + target.Status.ToString().ToLowerInvariant() + (string.IsNullOrEmpty(target.Error) ? "" : " (" + target.Error + ")");
                    return;
                }
                if (!string.IsNullOrEmpty(dep.Version))
                {
                    if (!VersionConstraint.TryParse(dep.Version, out var constraint))
                    {
                        Logger.Warning("Mod " + info.Id + ": unparsable version constraint \"" + dep.Version + "\" for dependency " + dep.Id + "; ignoring it.");
                    }
                    else if (!constraint.Satisfies(target.Info.Version))
                    {
                        container.Status = ModStatus.Skipped;
                        container.Error = "dependency " + dep.Id + " " + dep.Version + " required, but " + target.Info.VersionString + " is installed";
                        return;
                    }
                }
            }

            var stopwatch = Stopwatch.StartNew();
            Mod instance = null;
            try
            {
                Logger.Debug("Initializing " + info + " from " + info.AssemblyPath);
                instance = (Mod)Activator.CreateInstance(container.EntryType);
                instance.Info = info;
                instance.Logger = new ModLogger(info.Id);
                instance.Config = new ModConfig(Path.Combine(ConfigDirectory, info.Id + ".json"), instance.Logger);
                container.Instance = instance;
                container.Settings = instance.Config;

                instance.OnInitialize();

                if (instance.AutoPatch)
                {
                    instance.Harmony.PatchAll(container.Assembly);
                }
                container.PatchedMethodCount = instance.Harmony.GetPatchedMethods().Count();
                container.Status = ModStatus.Loaded;
                container.Error = null;
            }
            catch (Exception e)
            {
                container.Status = ModStatus.Failed;
                container.Exception = e;
                container.Error = ModLogger.Brief(e);
                Logger.Exception(e, "Mod " + info.Id + " failed to initialize");
                if (instance != null)
                {
                    try { instance.Harmony.UnpatchSelf(); }
                    catch (Exception unpatchError) { Logger.Debug("Unpatching " + info.Id + " failed: " + unpatchError.Message); }
                }
            }
            finally
            {
                container.InitializeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            }
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
