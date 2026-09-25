using System;
using System.Collections.Generic;
using System.Reflection;
using DnWModLoader.Logging;
using UnityEngine;

namespace DnWModLoader
{
    public enum LoaderPhase
    {
        NotStarted,
        Initializing,
        // All DnW mods are initialized
        Initialized,
        AllModsStarted,
        Running,
    }

    public static partial class ModLoader
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

        public static event Action<LoaderPhase> PhaseChanged;

        internal static ModLoaderBehaviour Behaviour;

        // The loader's MonoBehaviour. For coroutines or APIs that need a MonoBehaviour
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
    }
}
