using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using BepInLogging = BepInEx.Logging;
using LoaderLogLevel = DnWModLoader.Logging.LogLevel;

namespace DnWModLoader.BepInExCompat
{
    internal static class BepInExHost
    {
        public const string Framework = "BepInEx";

        private static readonly List<Plugin> Plugins = new List<Plugin>();
        private static readonly Dictionary<string, ScannedAssembly> AssembliesByName = new Dictionary<string, ScannedAssembly>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SharedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0Harmony", "BepInEx", "DnWModLoader", "Mono.Cecil" };
        private static bool _discovered;
        private static bool _started;

        private sealed class Plugin
        {
            public ScannedPlugin Scanned;
            public ModContainer Container;
            public PluginInfo Info;

            public string Guid { get { return Scanned.Guid; } }
        }

        public static void Discover(IList<string> modsFolderPlugins)
        {
            if (_discovered) return;
            _discovered = true;

            string pluginDirectory = Path.Combine(Path.Combine(ModLoader.GameDirectory, "BepInEx"), "plugins");
            var files = new List<string>();
            if (Directory.Exists(pluginDirectory))
            {
                try { files.AddRange(Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)); }
                catch (Exception e) { ModLoader.Logger.Warning("Could not list " + pluginDirectory + ": " + e.Message); }
            }
            files.AddRange(modsFolderPlugins);
            if (files.Count == 0) return;

            var stopwatch = Stopwatch.StartNew();
            SetUpEnvironment();
            Log(BepInLogging.LogLevel.Message, "BepInEx 5 plugin support by DnW Mod Loader " + ModLoader.Version);
            WarnAboutBepInExItself();

            var searchDirectories = new List<string> { ModLoader.LoaderDirectory, ModLoader.ManagedDirectory };
            searchDirectories.AddRange(files.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase));

            using (var scanner = new PluginScanner(searchDirectories))
            {
                var scannedPlugins = new List<ScannedPlugin>();
                foreach (var file in files)
                {
                    ScannedAssembly scanned;
                    try { scanned = scanner.Scan(file); }
                    catch (Exception e)
                    {
                        Log(BepInLogging.LogLevel.Warning, "Could not read " + file + ": " + e.Message);
                        continue;
                    }
                    if (scanned == null) continue;
                    if (!AssembliesByName.ContainsKey(scanned.Name) && !SharedAssemblies.Contains(scanned.Name)) AssembliesByName[scanned.Name] = scanned;
                    WarnAboutOtherBepInExVersions(scanned);
                    scannedPlugins.AddRange(scanned.Plugins);
                }

                Log(BepInLogging.LogLevel.Info, scannedPlugins.Count + " plugin" + (scannedPlugins.Count == 1 ? "" : "s") + " to load");
                Select(scannedPlugins);
                foreach (var plugin in Plugins)
                {
                    if (plugin.Container.Status != ModStatus.Discovered) continue;
                    try
                    {
                        plugin.Container.Assembly = LoadAssembly(plugin.Scanned.Assembly);
                    }
                    catch (Exception e)
                    {
                        Fail(plugin, "could not load " + Path.GetFileName(plugin.Scanned.Assembly.Path) + ": " + e.GetType().Name + ": " + e.Message, e);
                    }
                }
            }

            // The definitions are only needed while loading
            foreach (var scanned in AssembliesByName.Values) scanned.Definition = null;
            ModLoader.Logger.Debug("BepInEx plugin discovery took " + stopwatch.ElapsedMilliseconds + " ms.");
        }

        public static void Start(string phase)
        {
            if (!_discovered || _started) return;
            _started = true;
            if (Plugins.Count == 0) return;

            ModLoader.Logger.Debug("Starting BepInEx plugins during " + phase + ".");
            Log(BepInLogging.LogLevel.Message, "Chainloader started");
            try
            {
                var manager = new GameObject("BepInEx_Manager");
                UnityEngine.Object.DontDestroyOnLoad(manager);
                Chainloader.ManagerObject = manager;
                ThreadingHelper.Initialize();
            }
            catch (Exception e)
            {
                foreach (var plugin in Plugins.Where(p => p.Container.Status == ModStatus.Discovered)) Fail(plugin, "the BepInEx_Manager object could not be created: " + e.Message, e);
                LogSummary();
                return;
            }

            var notLoaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in Plugins)
            {
                if (plugin.Container.Status != ModStatus.Discovered)
                {
                    notLoaded.Add(plugin.Guid);
                    continue;
                }
                var broken = plugin.Scanned.Dependencies.FirstOrDefault(d => IsHard(d) && notLoaded.Contains(d.DependencyGUID));
                if (broken != null)
                {
                    Skip(plugin, "its dependency " + broken.DependencyGUID + " was not loaded");
                    notLoaded.Add(plugin.Guid);
                    continue;
                }
                if (!StartPlugin(plugin)) notLoaded.Add(plugin.Guid);
            }

            Log(BepInLogging.LogLevel.Message, "Chainloader startup complete");
            LogSummary();
        }

        private static void SetUpEnvironment()
        {
            string executable = Environment.GetEnvironmentVariable("DOORSTOP_PROCESS_PATH");
            if (string.IsNullOrEmpty(executable))
            {
                string dataFolder = Path.GetFileName(ModLoader.DataDirectory) ?? "";
                string processName = dataFolder.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) ? dataFolder.Substring(0, dataFolder.Length - 5) : "DragNWash";
                executable = Path.Combine(ModLoader.GameDirectory, processName + ".exe");
            }
            Paths.Initialize(Path.GetFullPath(executable), ModLoader.GameDirectory, ModLoader.ManagedDirectory, typeof(Paths).Assembly.Location);

            BepInLogging.Logger.Listeners.Add(new LoaderLogListener());
            // BepInEx log file
            try
            {
                var diskLog = new BepInLogging.DiskLogListener("LogOutput.log", BepInLogging.LogLevel.Fatal | BepInLogging.LogLevel.Error | BepInLogging.LogLevel.Warning | BepInLogging.LogLevel.Message | BepInLogging.LogLevel.Info);
                BepInLogging.Logger.Listeners.Add(diskLog);
                Application.quitting += diskLog.Dispose;
            }
            catch (Exception e) { ModLoader.Logger.Warning("Could not open BepInEx/LogOutput.log: " + e.Message); }

            AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembly;
        }

        private static void WarnAboutBepInExItself()
        {
            string patchers = Paths.PatcherPluginPath;
            try
            {
                if (Directory.Exists(patchers) && Directory.GetFiles(patchers, "*.dll", SearchOption.AllDirectories).Length > 0)
                    Log(BepInLogging.LogLevel.Warning, "Preloader patchers are not currently supported.");
            }
            catch { }
        }

        private static void WarnAboutOtherBepInExVersions(ScannedAssembly scanned)
        {
            if (scanned.Plugins.Count > 0 || scanned.Definition == null) return;
            var module = scanned.Definition.MainModule;
            if (module.AssemblyReferences.Any(r => r.Name == "BepInEx.Core" || r.Name == "BepInEx.Unity.Mono" || r.Name == "BepInEx.Unity.IL2CPP"))
                Log(BepInLogging.LogLevel.Warning, Path.GetFileName(scanned.Path) + "BepInEx 6 not currently supported.");
        }

        // Picks one version per GUID and sorts the plugin load order
        private static void Select(List<ScannedPlugin> scannedPlugins)
        {
            var candidates = new List<Plugin>();
            foreach (var scanned in scannedPlugins)
            {
                if (scanned.Invalid != null && scanned.Guid == null && scanned.Name == null)
                {
                    Log(BepInLogging.LogLevel.Warning, "Skipping over type [" + scanned.TypeName + "] as no metadata attribute is specified");
                    continue;
                }
                var plugin = new Plugin { Scanned = scanned, Container = CreateContainer(scanned) };
                ModLoader.AddExternalMod(plugin.Container);
                if (scanned.Invalid != null) Fail(plugin, "not a valid plugin: " + scanned.Invalid, null);
                candidates.Add(plugin);
            }

            var selected = new Dictionary<string, Plugin>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in candidates.Where(p => p.Container.Status == ModStatus.Discovered).GroupBy(p => p.Guid, StringComparer.OrdinalIgnoreCase))
            {
                Plugin chosen = null;
                foreach (var plugin in group.OrderByDescending(p => p.Scanned.Version))
                {
                    if (chosen != null)
                    {
                        Skip(plugin, "a newer version (" + chosen.Scanned.Version + ") is installed");
                        continue;
                    }
                    var processes = plugin.Scanned.Processes;
                    if (processes.Count > 0 && !processes.Any(p => string.Equals(p.ProcessName.Replace(".exe", ""), Paths.ProcessName, StringComparison.OrdinalIgnoreCase)))
                    {
                        Skip(plugin, "it only runs in " + string.Join(", ", processes.Select(p => p.ProcessName).ToArray()));
                        continue;
                    }
                    chosen = plugin;
                }
                if (chosen == null) continue;
                if (!ModLoader.ClaimModId(chosen.Container, out string owner))
                {
                    Fail(chosen, "its GUID is already used by " + owner, null);
                    continue;
                }
                if (ModLoader.Config.IsDisabled(chosen.Guid))
                {
                    chosen.Container.Status = ModStatus.Disabled;
                    chosen.Container.Error = "disabled in " + LoaderConfig.FileName;
                    continue;
                }
                selected[chosen.Guid] = chosen;
            }

            foreach (var plugin in selected.Values.OrderBy(p => p.Guid, StringComparer.OrdinalIgnoreCase).ToList())
            {
                var clash = plugin.Scanned.Incompatibilities.Select(i => i.IncompatibilityGUID)
                    .FirstOrDefault(id => !string.Equals(id, plugin.Guid, StringComparison.OrdinalIgnoreCase) && (selected.ContainsKey(id) || ModLoader.IsModLoaded(id)));
                if (clash == null) continue;
                selected.Remove(plugin.Guid);
                Fail(plugin, "it is incompatible with " + clash, null);
            }

            var ordered = Order(selected);
            var startable = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in ordered)
            {
                if (plugin.Container.Status != ModStatus.Discovered) continue;
                string notLoaded = null;
                var missing = new List<string>();
                foreach (var dependency in plugin.Scanned.Dependencies)
                {
                    if (!IsHard(dependency)) continue;
                    string id = dependency.DependencyGUID;
                    if (startable.TryGetValue(id, out var version))
                    {
                        if (VersionUtil.Compare(version, dependency.MinimumVersion) < 0) missing.Add(Describe(dependency));
                    }
                    else if (ModLoader.TryGetMod(id, out var mod))
                    {
                        if (mod.Framework == null && mod.Status == ModStatus.Loaded)
                        {
                            if (VersionUtil.Compare(mod.Info.Version, dependency.MinimumVersion) < 0) missing.Add(Describe(dependency));
                        }
                        else notLoaded = id;
                    }
                    else
                    {
                        missing.Add(Describe(dependency));
                    }
                }
                if (missing.Count > 0) Fail(plugin, "it has missing dependencies: " + string.Join(", ", missing.ToArray()), null, true);
                else if (notLoaded != null) Skip(plugin, "its dependency " + notLoaded + " was not loaded");
                else startable[plugin.Guid] = plugin.Scanned.Version;
            }

            Plugins.AddRange(ordered);
            Plugins.AddRange(candidates.Where(p => !ordered.Contains(p)));
        }

        // Dependencies first
        private static List<Plugin> Order(Dictionary<string, Plugin> selected)
        {
            var ordered = new List<Plugin>();
            var visiting = new List<Plugin>();
            var done = new HashSet<Plugin>();

            void Visit(Plugin plugin)
            {
                if (done.Contains(plugin)) return;
                int index = visiting.IndexOf(plugin);
                if (index >= 0)
                {
                    var cycle = visiting.Skip(index).ToList();
                    string chain = string.Join(" -> ", cycle.Select(p => p.Guid).Concat(new[] { plugin.Guid }).ToArray());
                    foreach (var member in cycle)
                        if (member.Container.Status == ModStatus.Discovered) Skip(member, "its dependencies form a cycle: " + chain);
                    return;
                }
                visiting.Add(plugin);
                foreach (var dependency in plugin.Scanned.Dependencies.OrderBy(d => d.DependencyGUID, StringComparer.OrdinalIgnoreCase))
                    if (selected.TryGetValue(dependency.DependencyGUID, out var target)) Visit(target);
                visiting.Remove(plugin);
                done.Add(plugin);
                ordered.Add(plugin);
            }

            foreach (var plugin in selected.Values.OrderBy(p => p.Guid, StringComparer.OrdinalIgnoreCase)) Visit(plugin);
            return ordered;
        }

        private static ModContainer CreateContainer(ScannedPlugin scanned)
        {
            string path = scanned.Assembly.Path;
            var manifest = new ModManifest
            {
                Id = scanned.Guid ?? Path.GetFileNameWithoutExtension(path),
                Name = scanned.Name ?? scanned.TypeName,
                Version = scanned.Version != null ? scanned.Version.ToString() : scanned.VersionText,
                Description = scanned.Assembly.Description,
            };
            return new ModContainer
            {
                Info = new ModInfo(manifest, Path.GetDirectoryName(path), path),
                Status = ModStatus.Discovered,
                Framework = Framework,
            };
        }

        private static Assembly LoadAssembly(ScannedAssembly scanned)
        {
            if (scanned.Loaded != null) return scanned.Loaded;
            if (scanned.Definition == null) return scanned.Loaded = Assembly.LoadFrom(scanned.Path);

            foreach (var missing in scanned.Missing)
                Log(BepInLogging.LogLevel.Warning, Path.GetFileName(scanned.Path) + " references " + missing + ", which is not supported.");

            var interop = HarmonyXInterop.Apply(scanned.Definition.MainModule);
            foreach (var member in interop.Unsupported)
                Log(BepInLogging.LogLevel.Warning, Path.GetFileName(scanned.Path) + " uses the HarmonyX-only " + member + ", which is not supported.");

            Assembly assembly;
            if (interop.Changed)
            {
                byte[] bytes;
                using (var stream = new MemoryStream())
                {
                    scanned.Definition.Write(stream);
                    bytes = stream.ToArray();
                }
                assembly = Assembly.Load(bytes);
                AssemblyLocations.Register(assembly, scanned.Path);
                ModLoader.Logger.Debug("Redirected HarmonyX members in " + Path.GetFileName(scanned.Path) + ": " + string.Join(", ", interop.Redirected.ToArray()));
            }
            else
            {
                assembly = Assembly.LoadFrom(scanned.Path);
            }
            scanned.Loaded = assembly;
            return assembly;
        }

        private static Assembly ResolvePluginAssembly(object sender, ResolveEventArgs args)
        {
            string name;
            try { name = new AssemblyName(args.Name).Name; }
            catch { return null; }
            if (!AssembliesByName.TryGetValue(name, out var scanned)) return null;
            try
            {
                return LoadAssembly(scanned);
            }
            catch (Exception e)
            {
                ModLoader.Logger.Warning("Failed to load " + scanned.Path + " while resolving " + args.Name + ": " + e.Message);
                return null;
            }
        }

        private static bool StartPlugin(Plugin plugin)
        {
            var container = plugin.Container;
            var stopwatch = Stopwatch.StartNew();
            string guid = plugin.Guid;
            string firstException = null;
            Application.LogCallback capture = (condition, stackTrace, type) =>
            {
                if (type == LogType.Exception && firstException == null) firstException = condition;
            };

            try
            {
                var type = container.Assembly.GetType(plugin.Scanned.TypeName, true);
                container.EntryType = type;
                plugin.Info = new PluginInfo
                {
                    Metadata = new BepInPlugin(guid, plugin.Scanned.Name, plugin.Scanned.VersionText),
                    Processes = plugin.Scanned.Processes,
                    Dependencies = plugin.Scanned.Dependencies,
                    Incompatibilities = plugin.Scanned.Incompatibilities,
                    Location = plugin.Scanned.Assembly.Path,
                    TypeName = plugin.Scanned.TypeName,
                    TargettedBepInExVersion = plugin.Scanned.TargetedBepInEx,
                };
                Chainloader.PluginInfos[guid] = plugin.Info;
                Log(BepInLogging.LogLevel.Info, "Loading [" + plugin.Info + "]");

                Component component;
                Application.logMessageReceived += capture;
                try { component = Chainloader.ManagerObject.AddComponent(type); }
                finally { Application.logMessageReceived -= capture; }

                var instance = component as BaseUnityPlugin;
                if (instance == null) throw new InvalidOperationException("Unity did not create the plugin component" + (firstException != null ? ": " + firstException : ""));
                plugin.Info.Instance = instance;
                Chainloader.AddLoaded(instance);

                container.Status = ModStatus.Loaded;
                container.Error = firstException != null ? "Awake threw " + firstException : null;
                container.Settings = new BepInExSettingsSource(instance.Config);
                container.PatchedMethodCount = CountPatches(guid);
                return true;
            }
            catch (Exception e)
            {
                Chainloader.PluginInfos.Remove(guid);
                Fail(plugin, e.GetType().Name + ": " + e.Message, e, true);
                return false;
            }
            finally
            {
                container.InitializeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        // GUID as Harmony id
        private static int CountPatches(string owner)
        {
            try
            {
                return Harmony.GetAllPatchedMethods().Count(method =>
                {
                    var info = Harmony.GetPatchInfo(method);
                    return info != null && info.Owners.Contains(owner);
                });
            }
            catch
            {
                return 0;
            }
        }

        private static void LogSummary()
        {
            foreach (var plugin in Plugins)
            {
                var c = plugin.Container;
                string line = c.Info + " [BepInEx]";
                switch (c.Status)
                {
                    case ModStatus.Loaded: ModLoader.Logger.Info("  [OK]       " + line + " (" + c.PatchedMethodCount + " patched method(s), " + c.InitializeMilliseconds.ToString("0") + " ms)" + (c.Error != null ? ": " + c.Error : "")); break;
                    case ModStatus.Disabled: ModLoader.Logger.Info("  [DISABLED] " + line); break;
                    case ModStatus.Skipped: ModLoader.Logger.Warning("  [SKIPPED]  " + line + ": " + c.Error); break;
                    case ModStatus.Failed: ModLoader.Logger.Error("  [FAILED]   " + line + ": " + c.Error); break;
                    default: ModLoader.Logger.Warning("  [?]        " + line); break;
                }
            }
        }

        private static void Skip(Plugin plugin, string reason)
        {
            plugin.Container.Status = ModStatus.Skipped;
            plugin.Container.Error = reason;
            string message = "Skipping [" + DisplayName(plugin) + "] because " + reason;
            Chainloader.DependencyErrors.Add(message);
            Log(BepInLogging.LogLevel.Warning, message);
        }

        private static void Fail(Plugin plugin, string reason, Exception exception, bool dependencyError = false)
        {
            plugin.Container.Status = ModStatus.Failed;
            plugin.Container.Error = reason;
            plugin.Container.Exception = exception;
            string message = "Could not load [" + DisplayName(plugin) + "] because " + reason;
            if (dependencyError) Chainloader.DependencyErrors.Add(message);
            Log(BepInLogging.LogLevel.Error, message);
            if (exception != null) Log(BepInLogging.LogLevel.Debug, exception);
        }

        private static string DisplayName(Plugin plugin)
        {
            return (plugin.Scanned.Name ?? plugin.Scanned.TypeName) + " " + (plugin.Scanned.Version != null ? plugin.Scanned.Version.ToString() : plugin.Scanned.VersionText);
        }

        private static string Describe(BepInDependency dependency)
        {
            var min = dependency.MinimumVersion;
            bool any = min == null || (min.Major == 0 && min.Minor == 0 && min.Build <= 0 && min.Revision <= 0);
            return any ? dependency.DependencyGUID : dependency.DependencyGUID + " (v" + min + " or newer)";
        }

        private static bool IsHard(BepInDependency dependency)
        {
            return (dependency.Flags & BepInDependency.DependencyFlags.HardDependency) != 0;
        }

        private static void Log(BepInLogging.LogLevel level, object message)
        {
            BepInLogging.Logger.Log(level, message);
        }

        // BepInEx log events into the loader's log
        private sealed class LoaderLogListener : BepInLogging.ILogListener
        {
            public void LogEvent(object sender, BepInLogging.LogEventArgs eventArgs)
            {
                if (eventArgs.Source is BepInLogging.UnityLogSource) return;
                DnWModLoader.Logging.Log.Write(ToLoaderLevel(eventArgs.Level), eventArgs.Source?.SourceName ?? "BepInEx", eventArgs.Data?.ToString() ?? "null");
            }

            private static LoaderLogLevel ToLoaderLevel(BepInLogging.LogLevel level)
            {
                switch (BepInLogging.LogLevelExtensions.GetHighestLevel(level))
                {
                    case BepInLogging.LogLevel.Fatal: return LoaderLogLevel.Fatal;
                    case BepInLogging.LogLevel.Error: return LoaderLogLevel.Error;
                    case BepInLogging.LogLevel.Warning: return LoaderLogLevel.Warning;
                    case BepInLogging.LogLevel.Debug: return LoaderLogLevel.Debug;
                    default: return LoaderLogLevel.Info;
                }
            }

            public void Dispose() { }
        }
    }
}
