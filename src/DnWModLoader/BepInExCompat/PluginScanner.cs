using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Mono.Cecil;

namespace DnWModLoader.BepInExCompat
{
    // DLL where BepInEx plugins are installed
    internal sealed class ScannedAssembly
    {
        public string Path;
        public string Name;
        public string Description;
        public AssemblyDefinition Definition;
        public System.Reflection.Assembly Loaded;
        public readonly List<ScannedPlugin> Plugins = new List<ScannedPlugin>();
        // Referenced assemblies and BepInEx members that are not available
        public readonly List<string> Missing = new List<string>();
    }

    // BaseUnityPlugin type without assembly
    internal sealed class ScannedPlugin
    {
        public ScannedAssembly Assembly;
        public string TypeName;
        public string Guid;
        public string Name;
        public string VersionText;
        public Version Version;
        public List<BepInDependency> Dependencies = new List<BepInDependency>();
        public List<BepInIncompatibility> Incompatibilities = new List<BepInIncompatibility>();
        public List<BepInProcess> Processes = new List<BepInProcess>();
        public Version TargetedBepInEx;
        // Why the metadata is unusable, or null
        public string Invalid;
    }

    internal sealed class PluginScanner : IDisposable
    {
        private const string PluginBaseType = "BepInEx.BaseUnityPlugin";
        private static readonly System.Text.RegularExpressions.Regex GuidPattern = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9\._\-]+$");

        private readonly DefaultAssemblyResolver _resolver = new DefaultAssemblyResolver();
        private readonly List<string> _directories = new List<string>();

        public PluginScanner(IEnumerable<string> searchDirectories)
        {
            foreach (var dir in _resolver.GetSearchDirectories()) _resolver.RemoveSearchDirectory(dir);
            foreach (var dir in searchDirectories)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || _directories.Contains(dir, StringComparer.OrdinalIgnoreCase)) continue;
                _directories.Add(dir);
                _resolver.AddSearchDirectory(dir);
            }
        }

        public ScannedAssembly Scan(string path)
        {
            AssemblyDefinition definition;
            try
            {
                definition = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = _resolver, InMemory = true, ReadingMode = ReadingMode.Deferred });
            }
            catch (BadImageFormatException)
            {
                return null;
            }

            var scanned = new ScannedAssembly { Path = path, Name = definition.Name.Name, Definition = definition };
            var module = definition.MainModule;
            var bepinexReference = module.AssemblyReferences.FirstOrDefault(r => r.Name == "BepInEx");
            if (bepinexReference == null) return scanned;

            foreach (var type in module.Types)
            {
                if (type.IsInterface || type.IsAbstract || !IsPlugin(type)) continue;
                scanned.Plugins.Add(ReadPlugin(type, scanned, bepinexReference.Version));
            }
            if (scanned.Plugins.Count > 0)
            {
                FindMissing(scanned);
                var description = definition.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.Reflection.AssemblyDescriptionAttribute");
                scanned.Description = description != null && description.ConstructorArguments.Count == 1 ? description.ConstructorArguments[0].Value as string : null;
            }
            return scanned;
        }

        private bool IsPlugin(TypeDefinition type)
        {
            var baseType = type.BaseType;
            for (int depth = 0; baseType != null && depth < 32; depth++)
            {
                if (baseType.FullName == PluginBaseType) return true;
                var resolved = TryResolve(baseType);
                if (resolved == null) return false;
                baseType = resolved.BaseType;
            }
            return false;
        }

        private ScannedPlugin ReadPlugin(TypeDefinition type, ScannedAssembly assembly, Version targetedBepInEx)
        {
            var plugin = new ScannedPlugin { Assembly = assembly, TypeName = type.FullName, TargetedBepInEx = targetedBepInEx };
            try
            {
                var metadata = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
                if (metadata == null)
                {
                    plugin.Invalid = "the type " + type.FullName + " has no [BepInPlugin] attribute";
                    return plugin;
                }
                plugin.Guid = metadata.ConstructorArguments[0].Value as string;
                plugin.Name = metadata.ConstructorArguments[1].Value as string;
                plugin.VersionText = metadata.ConstructorArguments[2].Value as string;
                plugin.Version = BepInPlugin.ParseVersion(plugin.VersionText);

                if (string.IsNullOrEmpty(plugin.Guid) || !GuidPattern.IsMatch(plugin.Guid)) plugin.Invalid = "its GUID \"" + plugin.Guid + "\" is of an illegal format";
                else if (plugin.Version == null) plugin.Invalid = "its version \"" + plugin.VersionText + "\" is invalid";
                else if (plugin.Name == null) plugin.Invalid = "its name is null";

                foreach (var attribute in AttributesWithInheritance(type))
                {
                    var args = attribute.ConstructorArguments;
                    switch (attribute.AttributeType.FullName)
                    {
                        case "BepInEx.BepInDependency":
                            string guid = (string)args[0].Value;
                            if (args.Count > 1 && args[1].Value is string minimum) plugin.Dependencies.Add(new BepInDependency(guid, minimum));
                            else plugin.Dependencies.Add(new BepInDependency(guid, args.Count > 1 ? (BepInDependency.DependencyFlags)Convert.ToInt32(args[1].Value) : BepInDependency.DependencyFlags.HardDependency));
                            break;
                        case "BepInEx.BepInIncompatibility":
                            plugin.Incompatibilities.Add(new BepInIncompatibility((string)args[0].Value));
                            break;
                        case "BepInEx.BepInProcess":
                            plugin.Processes.Add(new BepInProcess((string)args[0].Value));
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                plugin.Invalid = "its attributes could not be read (" + e.Message + ")";
            }
            return plugin;
        }

        // BepInEx dependencies, etc
        private IEnumerable<CustomAttribute> AttributesWithInheritance(TypeDefinition type)
        {
            for (int depth = 0; type != null && depth < 32; depth++)
            {
                foreach (var attribute in type.CustomAttributes) yield return attribute;
                if (type.BaseType == null || type.BaseType.FullName == PluginBaseType) yield break;
                type = TryResolve(type.BaseType);
            }
        }

        private void FindMissing(ScannedAssembly scanned)
        {
            var module = scanned.Definition.MainModule;
            foreach (var reference in module.AssemblyReferences)
            {
                if (IsAvailable(reference.Name)) continue;
                scanned.Missing.Add("assembly " + reference.Name + " " + reference.Version);
            }
            foreach (var member in module.GetMemberReferences())
            {
                var scope = member.DeclaringType?.Scope;
                if (scope == null || scope.Name != "BepInEx" || scope.MetadataScopeType != MetadataScopeType.AssemblyNameReference) continue;
                bool resolved;
                try { resolved = member is MethodReference method ? method.Resolve() != null : member is FieldReference field ? field.Resolve() != null : true; }
                catch (AssemblyResolutionException) { resolved = false; }
                if (!resolved) scanned.Missing.Add("BepInEx member " + member.FullName);
            }
        }

        private bool IsAvailable(string assemblyName)
        {
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (string.Equals(loaded.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)) return true; }
                catch { }
            }
            foreach (var dir in _directories)
                if (File.Exists(System.IO.Path.Combine(dir, assemblyName + ".dll"))) return true;
            return false;
        }

        private TypeDefinition TryResolve(TypeReference type)
        {
            try { return type.Resolve(); }
            catch (AssemblyResolutionException) { return null; }
        }

        public void Dispose()
        {
            _resolver.Dispose();
        }
    }
}
