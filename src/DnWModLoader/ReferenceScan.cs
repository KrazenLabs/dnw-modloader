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

        private const string NotPublic = " (not public in this loader)";

        public static void Collect(ModuleDefinition module, IList<string> directories, ICollection<string> missing)
        {
            if (module == null || missing == null) return;

            foreach (var reference in module.AssemblyReferences)
            {
                if (IsAvailable(reference.Name, directories)) continue;
                missing.Add("assembly " + reference.Name + " " + reference.Version);
            }

            var unusable = new Dictionary<string, string>(StringComparer.Ordinal);
            var order = new List<TypeReference>();
            foreach (var type in module.GetTypeReferences())
            {
                var scope = type.Scope;
                if (!IsBundled(scope) || unusable.ContainsKey(type.FullName)) continue;

                TypeDefinition definition;
                try { definition = type.Resolve(); }
                catch (AssemblyResolutionException) { definition = null; }
                if (definition == null) unusable[type.FullName] = scope.Name + " type " + type.FullName;
                else if (!IsVisible(definition)) unusable[type.FullName] = scope.Name + " type " + type.FullName + NotPublic;
                else continue;
                order.Add(type);
            }
            foreach (var type in order)
                if (!WithinUnusable(type.DeclaringType, unusable)) missing.Add(unusable[type.FullName]);

            foreach (var member in module.GetMemberReferences())
            {
                var scope = member.DeclaringType != null ? member.DeclaringType.Scope : null;
                if (!IsBundled(scope) || WithinUnusable(member.DeclaringType, unusable)) continue;

                var method = member as MethodReference;
                var field = member as FieldReference;
                if (method == null && field == null) continue;

                IMemberDefinition definition;
                try { definition = method != null ? (IMemberDefinition)method.Resolve() : field.Resolve(); }
                catch (AssemblyResolutionException) { definition = null; }
                if (definition == null) missing.Add(scope.Name + " member " + member.FullName);
                else if (!IsAccessible(definition)) missing.Add(scope.Name + " member " + member.FullName + NotPublic);
            }
        }

        private static bool IsBundled(IMetadataScope scope)
        {
            return scope != null && scope.MetadataScopeType == MetadataScopeType.AssemblyNameReference && BundledScopes.Contains(scope.Name);
        }

        private static bool WithinUnusable(TypeReference type, Dictionary<string, string> unusable)
        {
            for (; type != null; type = type.DeclaringType)
                if (unusable.ContainsKey(type.GetElementType().FullName)) return true;
            return false;
        }

        private static bool IsVisible(TypeDefinition type)
        {
            for (; type != null; type = type.DeclaringType)
            {
                if (!type.IsNested) return type.IsPublic;
                if (!type.IsNestedPublic && !type.IsNestedFamily && !type.IsNestedFamilyOrAssembly) return false;
            }
            return false;
        }

        private static bool IsAccessible(IMemberDefinition member)
        {
            var method = member as MethodDefinition;
            if (method != null) return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
            var field = member as FieldDefinition;
            return field != null && (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly);
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
