using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DnWModLoader.Logging;

namespace DnWModLoader
{
    internal static class AssemblyResolver
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Assembly> LoadedByName = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Directories = new List<string>();
        private static readonly List<Func<string, string, Assembly>> Fallbacks = new List<Func<string, string, Assembly>>();
        private static string _loaderDirectory;
        private static bool _tracking;
        private static bool _installed;

        public static ModLogger Logger { get; set; }

        public static void Install(string loaderDirectory)
        {
            lock (Sync)
            {
                if (_loaderDirectory == null && !string.IsNullOrEmpty(loaderDirectory)) _loaderDirectory = loaderDirectory;
                if (_installed) return;
                _installed = true;
            }
            Track();
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        public static void AddDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            lock (Sync)
            {
                foreach (var existing in Directories)
                    if (string.Equals(existing, directory, StringComparison.OrdinalIgnoreCase)) return;
                Directories.Add(directory);
            }
        }

        public static void AddFallback(Func<string, string, Assembly> fallback)
        {
            if (fallback == null) return;
            lock (Sync) Fallbacks.Add(fallback);
        }

        public static Assembly FindLoaded(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Track();
            lock (Sync) return LoadedByName.TryGetValue(name, out var assembly) ? assembly : null;
        }

        public static bool IsLoaded(string name)
        {
            return FindLoaded(name) != null;
        }

        private static void Track()
        {
            lock (Sync)
            {
                if (_tracking) return;
                _tracking = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) Remember(assembly);
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            Remember(args.LoadedAssembly);
        }

        private static void Remember(Assembly assembly)
        {
            string name;
            try { name = assembly.GetName().Name; }
            catch { return; }
            if (string.IsNullOrEmpty(name)) return;
            lock (Sync)
                if (!LoadedByName.ContainsKey(name)) LoadedByName.Add(name, assembly);
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string name;
            try { name = new AssemblyName(args.Name).Name; }
            catch { return null; }
            if (string.IsNullOrEmpty(name) || name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;

            var assembly = LoadFrom(_loaderDirectory, name, args.Name, false) ?? FindLoaded(name);
            if (assembly != null) return assembly;

            string[] directories;
            Func<string, string, Assembly>[] fallbacks;
            lock (Sync)
            {
                directories = Directories.ToArray();
                fallbacks = Fallbacks.ToArray();
            }
            foreach (var directory in directories)
            {
                assembly = LoadFrom(directory, name, args.Name, true);
                if (assembly != null) return assembly;
            }
            foreach (var fallback in fallbacks)
            {
                try { assembly = fallback(name, args.Name); }
                catch (Exception e)
                {
                    Report(LogLevel.Warning, "Resolving " + args.Name + " failed: " + e.Message);
                    assembly = null;
                }
                if (assembly != null) return assembly;
            }
            return null;
        }

        private static Assembly LoadFrom(string directory, string name, string requested, bool reportSuccess)
        {
            if (string.IsNullOrEmpty(directory)) return null;
            string path = Path.Combine(directory, name + ".dll");
            if (!File.Exists(path)) return null;
            try
            {
                var assembly = Assembly.LoadFrom(path);
                if (reportSuccess) Report(LogLevel.Debug, "Resolved " + name + " from " + path);
                return assembly;
            }
            catch (Exception e)
            {
                Report(LogLevel.Warning, "Failed to load " + path + " while resolving " + requested + ": " + e.Message);
                return null;
            }
        }

        private static void Report(LogLevel level, string message)
        {
            var logger = Logger;
            if (logger != null) logger.Write(level, message);
            else Preloader.Note(message);
        }
    }
}
