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
        public const string Version = "1.5.0";

        public static readonly Version ParsedVersion = new Version(1, 5, 0);

        public const string ModsFolderName = "Mods";
        public const string ConfigFolderName = "config";
        public const string LogFileName = "ModLoader.log";

        private static readonly List<ModContainer> ModList = new List<ModContainer>();
        private static readonly Dictionary<string, ModContainer> ModsById = new Dictionary<string, ModContainer>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> ResolveDirectories = new List<string>();
        private static ModContainer[] _loadedCache = new ModContainer[0];
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
            foreach (var c in _loadedCache)
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
            Directory.CreateDirectory(ModsDirectory);
            Directory.CreateDirectory(ConfigDirectory);
            Log.Open(Path.Combine(ModsDirectory, LogFileName));

            Config = LoaderConfig.Load(Path.Combine(ModsDirectory, LoaderConfig.FileName), Logger);
            Log.MinimumLevel = Config.LogLevel;
            Log.EchoToUnity = Config.EchoLoaderLogToUnity;

            LogHeader();
            HookUnityLog();
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

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

            Phase = LoaderPhase.Initialized;
            CountStatuses(out int loaded, out int failed, out int skipped, out int disabled);
            int waiting = ModList.Count(c => c.Framework != null && c.Status == ModStatus.Discovered);
            Logger.Info("Initialization finished in " + stopwatch.ElapsedMilliseconds + " ms: " + loaded + " loaded, " + failed + " failed, " + skipped + " skipped, " + disabled + " disabled"
                        + (waiting > 0 ? ", " + waiting + " BepInEx plugin(s) found." : "."));

            try { ModsInitialized?.Invoke(); }
            catch (Exception e) { Logger.Exception(e, "ModsInitialized exception"); }

            HookSceneEvents();
            EnsureBehaviour("SubsystemRegistration");

            if (!Preloader.AfterRegistrationHooked) StartBepInExPlugins("SubsystemRegistration");
        }

        internal static void AfterRegistration()
        {
            StartBepInExPlugins(Preloader.AfterRegistrationPhase ?? "a later initializer");
        }

        internal static void GameStarted()
        {
            if (Phase >= LoaderPhase.Running) return;
            Phase = LoaderPhase.Running;
            Logger.Info("First scene loaded: " + SafeActiveSceneName() + ". Game is running.");
            Dispatch("OnGameStarted", GameStartedAction);
        }

        internal static void Shutdown()
        {
            Logger.Info("Application quitting.");
            Log.Close();
        }

        private static readonly Action<Mod> GameStartedAction = m => m.OnGameStarted();
        private static readonly Action<Mod> AllModsInitializedAction = m => m.OnAllModsInitialized();

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
            try { Logger.Debug("OS: " + SystemInfo.operatingSystem + " | CLR: " + Environment.Version + " | 64-bit: " + Environment.Is64BitProcess); } catch { }
            try { Logger.Debug("Command line: " + string.Join(" ", Environment.GetCommandLineArgs())); } catch { }
            foreach (var line in Preloader.TakeEarlyLog()) Logger.Debug("[preloader] " + line);
            Logger.Debug("Game directory: " + GameDirectory);
            Logger.Debug("Loader directory: " + LoaderDirectory);
            Logger.Debug("Mods directory: " + ModsDirectory);
            Logger.Debug("Config: hotkey=" + Config.OverlayHotkey + " logLevel=" + Config.LogLevel + " mirrorUnityLog=" + Config.MirrorUnityLog
                         + " disabledMods=[" + string.Join(", ", Config.DisabledMods) + "]");
        }

        private static void HookUnityLog()
        {
            if (Config.MirrorUnityLog == UnityLogMirror.None) return;
            try { Application.logMessageReceivedThreaded += OnUnityLogMessage; }
            catch (Exception e) { Logger.Debug("Could not hook Unity log: " + e.Message); }
        }

        private static void OnUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            if (Log.IsEchoedLine(condition)) return;
            var mirror = Config.MirrorUnityLog;
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

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string name;
            try { name = new AssemblyName(args.Name).Name; }
            catch { return null; }
            if (string.IsNullOrEmpty(name) || name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;

            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return loaded; }
                catch { }
            }

            foreach (var dir in ResolveDirectories)
            {
                string candidate = Path.Combine(dir, name + ".dll");
                if (!File.Exists(candidate)) continue;
                try
                {
                    var assembly = Assembly.LoadFrom(candidate);
                    Logger.Debug("Resolved " + name + " from " + candidate);
                    return assembly;
                }
                catch (Exception e)
                {
                    Logger.Warning("Failed to load " + candidate + " while resolving " + args.Name + ": " + e.Message);
                }
            }
            return null;
        }

        private static void AddResolveDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            foreach (var existing in ResolveDirectories)
                if (string.Equals(existing, dir, StringComparison.OrdinalIgnoreCase)) return;
            ResolveDirectories.Add(dir);
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
            public string ManifestPath;
            public ModManifest Manifest;
            public string AssemblyPath;
            public bool IsBare;
            public string DiscoveryError;
        }

        private static readonly string[] ReservedDllNames = { "DnWModLoader.dll", "0Harmony.dll", "BepInEx.dll" };

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

        internal static void AddExternalMod(ModContainer container)
        {
            ModList.Add(container);
        }

        internal static bool ClaimModId(ModContainer container, out string owner)
        {
            owner = null;
            if (ModsById.TryGetValue(container.Info.Id, out var existing))
            {
                owner = existing.Info.ToString();
                return false;
            }
            ModsById[container.Info.Id] = container;
            return true;
        }

        private static bool ReferencesBepInEx(string path)
        {
            try
            {
                using (var module = Mono.Cecil.ModuleDefinition.ReadModule(path, new Mono.Cecil.ReaderParameters { ReadingMode = Mono.Cecil.ReadingMode.Deferred }))
                    return module.AssemblyReferences.Any(r => r.Name == "BepInEx");
            }
            catch
            {
                return false;
            }
        }

        private static void DiscoverAndLoadMods()
        {
            var candidates = Discover();
            Logger.Info("Discovered " + candidates.Count + " mod candidate(s) in " + ModsDirectory);

            AddResolveDirectory(LoaderDirectory);
            AddResolveDirectory(ModsDirectory);
            foreach (var candidate in candidates)
            {
                AddResolveDirectory(candidate.Directory);
                AddResolveDirectory(Path.Combine(candidate.Directory, "lib"));
                AddResolveDirectory(Path.Combine(candidate.Directory, "libs"));
            }

            var containers = new List<ModContainer>();
            foreach (var candidate in candidates)
            {
                var container = LoadCandidate(candidate);
                if (container != null) containers.Add(container);
            }

            // Handles duplicate ids
            foreach (var container in containers)
            {
                string id = container.Info?.Id;
                if (string.IsNullOrEmpty(id)) { ModList.Add(container); continue; }
                if (ModsById.TryGetValue(id, out var first))
                {
                    container.Status = ModStatus.Failed;
                    container.Error = "Duplicate mod id; already provided by " + first.Info.AssemblyPath;
                    Logger.Error("Mod id " + id + " is used twice: " + first.Info.AssemblyPath + " and " + container.Info.AssemblyPath);
                    ModList.Add(container);
                    continue;
                }
                ModsById[id] = container;
            }

            var ordered = OrderForLoading(containers.Where(c => c.Status == ModStatus.Discovered).ToList());

            var rest = containers.Where(c => !ordered.Contains(c)).ToList();
            ModList.Clear();
            ModList.AddRange(ordered);
            ModList.AddRange(rest);

            foreach (var container in ordered) InitializeMod(container);
            RefreshLoadedCache();

            foreach (var container in _loadedCache)
            {
                try { container.Instance.OnAllModsInitialized(); }
                catch (Exception e)
                {
                    container.Error = "OnAllModsInitialized threw: " + e.GetType().Name + ": " + e.Message;
                    Logger.Exception(e, "Mod " + container.Info.Id + " threw in OnAllModsInitialized");
                }
            }

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
                    var candidate = new Candidate { Directory = dir, ManifestPath = manifestPath };
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
                foreach (var dll in dlls.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    if (IsReservedDll(dll)) continue;
                    if (ReferencesBepInEx(dll)) { ModsFolderBepInExPlugins.Add(dll); continue; }
                    result.Add(new Candidate { Directory = dir, AssemblyPath = dll, IsBare = true });
                }
            }

            foreach (var dll in SafeGetFiles(ModsDirectory, "*.dll").OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (IsReservedDll(dll)) continue;
                if (ReferencesBepInEx(dll)) { ModsFolderBepInExPlugins.Add(dll); continue; }
                result.Add(new Candidate { Directory = ModsDirectory, AssemblyPath = dll, IsBare = true });
            }
            return result;
        }

        private static bool IsReservedDll(string path)
        {
            string file = Path.GetFileName(path);
            foreach (var reserved in ReservedDllNames)
            {
                if (string.Equals(file, reserved, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warning("Ignoring " + path + ": " + reserved + " must not be placed in the Mods folder (it is part of the loader).");
                    return true;
                }
            }
            return false;
        }

        private static string[] SafeGetFiles(string dir, string pattern)
        {
            try { return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch { return new string[0]; }
        }

        private static string PickAssembly(string dir, string folderName, out string error)
        {
            error = null;
            var dlls = SafeGetFiles(dir, "*.dll").Where(d => !IsReservedDllQuiet(d)).ToArray();
            if (dlls.Length == 0) { error = "the mod folder contains no DLL"; return null; }
            if (dlls.Length == 1) return dlls[0];
            foreach (var dll in dlls)
                if (string.Equals(Path.GetFileNameWithoutExtension(dll), folderName, StringComparison.OrdinalIgnoreCase)) return dll;
            error = "the mod folder contains several DLLs; set \"assembly\" in mod.json";
            return null;
        }

        private static bool IsReservedDllQuiet(string path)
        {
            string file = Path.GetFileName(path);
            foreach (var reserved in ReservedDllNames)
                if (string.Equals(file, reserved, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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
                    Logger.Debug("Ignoring " + candidate.AssemblyPath + " (no Mod subclass; treated as a library)");
                    return null;
                }
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = assembly.GetName().Name.ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, entryError ?? "no class deriving from DnWModLoader.Mod found in " + Path.GetFileName(candidate.AssemblyPath));
            }
            container.EntryType = entryType;

            var attribute = entryType.GetCustomAttribute<ModInfoAttribute>();
            var effective = candidate.Manifest ?? new ModManifest { Version = null };
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
                container.Error = e.GetType().Name + ": " + e.Message;
                Logger.Exception(e, "Mod " + info.Id + " failed to initialize");
                if (instance != null)
                {
                    try { instance.Harmony.UnpatchAll(info.Id); }
                    catch (Exception unpatchError) { Logger.Debug("Unpatching " + info.Id + " failed: " + unpatchError.Message); }
                }
            }
            finally
            {
                container.InitializeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        private static void RefreshLoadedCache()
        {
            _loadedCache = ModList.Where(c => c.CallbacksEnabled).ToArray();
        }

        internal static void Dispatch(string callback, Action<Mod> action)
        {
            var mods = _loadedCache;
            for (int i = 0; i < mods.Length; i++)
            {
                var container = mods[i];
                if (container.IsCallbackDisabled(callback)) continue;
                try
                {
                    action(container.Instance);
                    container.ResetFailures(callback);
                }
                catch (Exception e)
                {
                    container.RecordFailure(callback, e);
                }
            }
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
                    Dispatch("OnSceneLoaded", m => m.OnSceneLoaded(scene, mode));
                };
                SceneManager.sceneUnloaded += scene =>
                {
                    Logger.Debug("Scene unloaded: " + scene.name);
                    Dispatch("OnSceneUnloaded", m => m.OnSceneUnloaded(scene));
                };
            }
            catch (Exception e)
            {
                Logger.Warning("Could not subscribe to scene events: " + e.Message);
            }
        }
    }
}
