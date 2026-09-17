using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace DnWModLoader
{
    // Checks if a mod's references are supported
    internal static class ReferenceScan
    {
        internal static readonly HashSet<string> BundledScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BepInEx", "MelonLoader", "0Harmony", "MonoMod.Utils", "MonoMod.RuntimeDetour", "MonoMod.Core", "Tomlet",
        };

        public static void Collect(ModuleDefinition module, IList<string> directories, ICollection<string> missing)
        {
            if (module == null || missing == null) return;

            foreach (var reference in module.AssemblyReferences)
            {
                if (IsAvailable(reference.Name, directories)) continue;
                missing.Add("assembly " + reference.Name + " " + reference.Version);
            }

            foreach (var member in module.GetMemberReferences())
            {
                var scope = member.DeclaringType != null ? member.DeclaringType.Scope : null;
                if (scope == null || scope.MetadataScopeType != MetadataScopeType.AssemblyNameReference) continue;
                if (!BundledScopes.Contains(scope.Name)) continue;

                bool resolved;
                try
                {
                    resolved = member is MethodReference method ? method.Resolve() != null
                        : member is FieldReference field ? field.Resolve() != null
                        : true;
                }
                catch (AssemblyResolutionException) { resolved = false; }
                if (!resolved) missing.Add(scope.Name + " member " + member.FullName);
            }
        }

        public static bool IsAvailable(string assemblyName, IList<string> directories)
        {
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (string.Equals(loaded.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)) return true; }
                catch { }
            }
            if (directories != null)
            {
                foreach (var dir in directories)
                    if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, assemblyName + ".dll"))) return true;
            }
            return false;
        }

        // Scans without loading
        public static List<string> Scan(string path, IList<string> directories)
        {
            var missing = new List<string>();
            var resolver = new DefaultAssemblyResolver();
            try
            {
                foreach (var dir in resolver.GetSearchDirectories()) resolver.RemoveSearchDirectory(dir);
                if (directories != null)
                    foreach (var dir in directories)
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) resolver.AddSearchDirectory(dir);

                var parameters = new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadingMode = ReadingMode.Deferred };
                using (var definition = AssemblyDefinition.ReadAssembly(path, parameters))
                    Collect(definition.MainModule, directories, missing);
            }
            catch (Exception e)
            {
                ModLoader.Logger.Debug("Could not pre-scan " + Path.GetFileName(path) + ": " + e.Message);
            }
            finally
            {
                resolver.Dispose();
            }
            return missing;
        }
    }
}
