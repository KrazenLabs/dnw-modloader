using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DnWModLoader
{
    public static partial class ModLoader
    {
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
    }
}
