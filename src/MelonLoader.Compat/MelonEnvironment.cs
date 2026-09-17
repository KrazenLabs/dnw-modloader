using System;
using System.IO;
using UnityEngine;

namespace MelonLoader.Utils
{
    public static class MelonEnvironment
    {
        internal static string GameRoot = "";
        internal static string LoaderDirectory = "";

        public static string GameRootDirectory { get { return GameRoot; } }
        public static string MelonBaseDirectory { get { return GameRoot; } }
        public static string MelonLoaderDirectory { get { return LoaderDirectory; } }
        public static string OurRuntimeDirectory { get { return LoaderDirectory; } }
        public static string MelonManagedDirectory { get { return LoaderDirectory; } }
        public static string DependenciesDirectory { get { return Combine(LoaderDirectory, "Dependencies"); } }
        public static string SupportModuleDirectory { get { return Combine(DependenciesDirectory, "SupportModules"); } }
        public static string CompatibilityLayerDirectory { get { return Combine(DependenciesDirectory, "CompatibilityLayers"); } }
        public static string Il2CppAssemblyGeneratorDirectory { get { return Combine(DependenciesDirectory, "Il2CppAssemblyGenerator"); } }
        public static string Il2CppAssembliesDirectory { get { return Combine(GameRoot, "MelonLoader/Il2CppAssemblies"); } }
        public static string Il2CppDataDirectory { get { return Combine(UnityGameDataDirectory, "il2cpp_data"); } }

        public static string ModsDirectory { get { return Combine(GameRoot, "Mods"); } }
        public static string PluginsDirectory { get { return Combine(GameRoot, "Plugins"); } }
        public static string UserLibsDirectory { get { return Combine(GameRoot, "UserLibs"); } }
        public static string UserDataDirectory { get { return Combine(GameRoot, "UserData"); } }
        public static string MelonLoaderLogsDirectory { get { return Combine(LoaderDirectory, "Logs"); } }

        public static string GameExecutablePath
        {
            get
            {
                try { return Environment.GetCommandLineArgs()[0]; }
                catch { return ""; }
            }
        }

        public static string GameExecutableName
        {
            get
            {
                try { return Path.GetFileNameWithoutExtension(GameExecutablePath); }
                catch { return ""; }
            }
        }

        public static string UnityGameDataDirectory
        {
            get
            {
                try { return Application.dataPath; }
                catch { return ""; }
            }
        }

        public static string UnityGameManagedDirectory { get { return Combine(UnityGameDataDirectory, "Managed"); } }
        public static string UnityPlayerPath { get { return Combine(GameRoot, "UnityPlayer.dll"); } }

        // Drag'n Wash runs on Mono, never on CoreCLR
        public static bool IsMonoRuntime { get { return true; } }
        public static bool IsDotnetRuntime { get { return false; } }

        private static string Combine(string root, string relative)
        {
            if (string.IsNullOrEmpty(root)) return "";
            return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}

namespace MelonLoader.InternalUtils
{
    public static class UnityInformationHandler
    {
        public static string GameName { get { return Safe(() => Application.productName); } }
        public static string GameDeveloper { get { return Safe(() => Application.companyName); } }
        public static string GameVersion { get { return Safe(() => Application.version); } }
        public static string EngineVersionString { get { return Safe(() => Application.unityVersion); } }

        private static string Safe(Func<string> get)
        {
            try { return get(); }
            catch { return ""; }
        }
    }
}
