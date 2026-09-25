using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DnWModLoader
{
    public static partial class ModLoader
    {
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

        internal static void Register(ModContainer container)
        {
            var info = container.Info;
            if (container.Status == ModStatus.Discovered && info != null && Config != null && Config.IsDisabled(info.Id))
                container.MarkDisabled("disabled in " + LoaderConfig.FileName);
            if (!ClaimModId(container, out string owner) && container.Status == ModStatus.Discovered)
            {
                container.MarkFailed("Duplicate mod id; already provided by " + owner);
                Logger.Error("Mod id " + info.Id + " is used twice: " + owner + " and " + Describe(container));
            }
            if (!ModList.Contains(container)) ModList.Add(container);
            InvalidateLoadedCache();
        }

        internal static void MoveToEnd(IEnumerable<ModContainer> containers)
        {
            var moved = containers.ToList();
            foreach (var container in moved) ModList.Remove(container);
            ModList.AddRange(moved);
            InvalidateLoadedCache();
        }

        internal static void LogSummary(ModContainer container)
        {
            string name = container.Info != null ? container.Info.ToString() : Path.GetFileName(container.Assembly?.Location ?? "?");
            if (container.Framework != null) name += " [" + container.Framework + "]";
            string reason = string.IsNullOrEmpty(container.Error) ? "" : ": " + container.Error;
            switch (container.Status)
            {
                case ModStatus.Loaded: Logger.Info("  [OK]       " + name + " (" + container.PatchedMethodCount + " patched method(s), " + container.InitializeMilliseconds.ToString("0") + " ms)" + reason); break;
                case ModStatus.Disabled: Logger.Info("  [DISABLED] " + name + reason); break;
                case ModStatus.Skipped: Logger.Warning("  [SKIPPED]  " + name + reason); break;
                case ModStatus.Failed: Logger.Error("  [FAILED]   " + name + reason); break;
                default: Logger.Warning("  [?]        " + name + reason); break;
            }
        }

        private static string Describe(ModContainer container)
        {
            var info = container.Info;
            if (info == null) return "?";
            return string.IsNullOrEmpty(info.AssemblyPath) ? info.ToString() : info + " from " + info.AssemblyPath;
        }

        private static bool ClaimModId(ModContainer container, out string owner)
        {
            owner = null;
            string id = container.Info?.Id;
            if (string.IsNullOrEmpty(id)) return true;
            if (ModsById.TryGetValue(id, out var existing) && !ReferenceEquals(existing, container) && ClaimRank(container) <= ClaimRank(existing))
            {
                owner = Describe(existing);
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
    }
}
