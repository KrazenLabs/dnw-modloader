using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace DnWModLoader
{
    internal static class ParallelLoaderWarning
    {
        public const string Banner = "Another mod loader is installed together with the DnW Mod Loader. This WILL cause conflicts and bugs. Please remove the other loader.";

        private static bool _checked;
        private static string _found;

        public static string Found
        {
            get
            {
                if (!_checked)
                {
                    _checked = true;
                    try { _found = Detect(); }
                    catch (Exception) { _found = null; }
                }
                return _found;
            }
        }

        public static bool Applies() { return !string.IsNullOrEmpty(Found); }

        public static string LogMessage
        {
            get
            {
                return "Another mod loader is installed in the game folder: " + Found + ". Please remove the other loader. Mods made for other loaders should still work with the DnW loader.";
            }
        }

        private static string Detect()
        {
            string game = ModLoader.GameDirectory;
            if (string.IsNullOrEmpty(game) || !Directory.Exists(game)) return null;

            var found = new List<string>();

            // MelonLoader
            bool melonProxy = File.Exists(Path.Combine(game, "version.dll")) && File.Exists(Path.Combine(game, "dobby.dll"));
            bool melonRuntime = FileExistsIn(Path.Combine(game, "MelonLoader"), "MelonLoader.dll");
            if (melonProxy || melonRuntime)
            {
                found.Add("MelonLoader (" + (melonProxy && melonRuntime ? "version.dll + dobby.dll and a MelonLoader folder"
                    : melonProxy ? "version.dll + dobby.dll" : "a MelonLoader folder") + ")");
            }

            // BepInEx
            string core = Path.Combine(Path.Combine(game, "BepInEx"), "core");
            if (File.Exists(Path.Combine(core, "BepInEx.Preloader.dll")) || File.Exists(Path.Combine(core, "BepInEx.dll")))
                found.Add("BepInEx (BepInEx\\core)");

            // MelonLoader assembly
            string live = LiveForeignMelonLoader();
            if (live != null) found.Add("a running MelonLoader (" + live + ")");

            return found.Count == 0 ? null : string.Join(", ", found.ToArray());
        }

        private static string LiveForeignMelonLoader()
        {
            string ours = ModLoader.LoaderDirectory;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!string.Equals(assembly.GetName().Name, "MelonLoader", StringComparison.OrdinalIgnoreCase)) continue;
                    string location = assembly.Location;
                    if (string.IsNullOrEmpty(location)) continue;
                    string directory = Path.GetDirectoryName(Path.GetFullPath(location));
                    if (!string.IsNullOrEmpty(ours) && string.Equals(directory, Path.GetFullPath(ours), StringComparison.OrdinalIgnoreCase)) continue;
                    return location;
                }
                catch (Exception) { }
            }
            return null;
        }

        private static bool FileExistsIn(string root, string fileName)
        {
            try
            {
                return Directory.Exists(root) && Directory.GetFiles(root, fileName, SearchOption.AllDirectories).Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
