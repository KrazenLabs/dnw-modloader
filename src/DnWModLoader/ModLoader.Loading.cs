using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using DnWModLoader.Config;
using DnWModLoader.Logging;

namespace DnWModLoader
{
    public static partial class ModLoader
    {
        private static void DiscoverAndLoadMods()
        {
            var candidates = Discover();
            Logger.Info("Discovered " + candidates.Count + " mod candidate(s) in " + ModsDirectory);

            AssemblyResolver.AddDirectory(ModsDirectory);
            foreach (var candidate in candidates)
            {
                AssemblyResolver.AddDirectory(candidate.Directory);
                AssemblyResolver.AddDirectory(Path.Combine(candidate.Directory, "lib"));
                AssemblyResolver.AddDirectory(Path.Combine(candidate.Directory, "libs"));
            }

            var containers = new List<ModContainer>();
            foreach (var candidate in candidates)
            {
                ModContainer container;
                try
                {
                    container = LoadCandidate(candidate);
                }
                catch (Exception e)
                {
                    Logger.Exception(e, "Loading " + (candidate.AssemblyPath ?? candidate.Directory) + " failed");
                    container = Fail(new ModContainer { Info = FallbackInfo(candidate) }, "could not be loaded: " + ModLogger.Brief(e), e);
                }
                if (container != null) containers.Add(container);
            }

            // Handles duplicate ids
            foreach (var container in containers) Register(container);

            var ordered = OrderForLoading(containers.Where(c => c.Status == ModStatus.Discovered).ToList());

            var rest = containers.Where(c => !ordered.Contains(c)).ToList();
            var loadOrder = ordered.Concat(rest).ToList();
            MoveToEnd(loadOrder);

            foreach (var container in ordered) InitializeMod(container);
            Dispatch(nameof(Mod.OnAllModsInitialized), m => m.OnAllModsInitialized());

            foreach (var container in loadOrder) LogSummary(container);
        }

        private static ModContainer LoadCandidate(Candidate candidate)
        {
            var container = new ModContainer { Status = ModStatus.Discovered };

            if (candidate.DiscoveryError != null)
            {
                container.Info = candidate.Manifest != null && !string.IsNullOrEmpty(candidate.Manifest.Id)
                    ? new ModInfo(candidate.Manifest, candidate.Directory, candidate.AssemblyPath)
                    : new ModInfo(new ModManifest { Id = Path.GetFileName(candidate.Directory).ToLowerInvariant(), Name = Path.GetFileName(candidate.Directory) }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, candidate.DiscoveryError);
            }

            // Manifest mods can be disabled without loading their assembly
            if (candidate.Manifest != null)
            {
                var manifest = candidate.Manifest;
                if (!ModManifest.IsValidId(manifest.Id))
                {
                    container.Info = new ModInfo(new ModManifest { Id = Path.GetFileName(candidate.Directory).ToLowerInvariant(), Name = manifest.Name ?? Path.GetFileName(candidate.Directory), Version = manifest.Version }, candidate.Directory, candidate.AssemblyPath);
                    return Fail(container, "mod.json has a missing or invalid \"id\" (use lower-case letters, digits, '.', '_' or '-'): " + (manifest.Id ?? "(null)"));
                }
                if (!manifest.Enabled || Config.IsDisabled(manifest.Id))
                {
                    container.Info = new ModInfo(manifest, candidate.Directory, candidate.AssemblyPath);
                    container.MarkDisabled(!manifest.Enabled ? "disabled in mod.json" : "disabled in " + LoaderConfig.FileName);
                    return container;
                }
            }

            if (string.IsNullOrEmpty(candidate.AssemblyPath) || !File.Exists(candidate.AssemblyPath))
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = Path.GetFileName(candidate.Directory).ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, "assembly not found: " + (candidate.AssemblyPath ?? "(none)"));
            }

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(candidate.AssemblyPath);
            }
            catch (Exception e)
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = Path.GetFileNameWithoutExtension(candidate.AssemblyPath).ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, "could not load assembly: " + e.GetType().Name + ": " + e.Message, e);
            }

            string loadedFrom = LoadedFrom(assembly);
            if (!SamePath(loadedFrom, candidate.AssemblyPath))
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = assembly.GetName().Name.ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, "assembly name " + assembly.GetName().Name + " is already used by " + (string.IsNullOrEmpty(loadedFrom) ? "an assembly loaded from memory" : loadedFrom));
            }
            container.Assembly = assembly;

            Type entryType;
            string entryError;
            try
            {
                entryType = FindEntryType(assembly, candidate.Manifest?.EntryType, out entryError);
            }
            catch (Exception e)
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = assembly.GetName().Name.ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, "could not inspect assembly: " + ModLogger.Describe(e), e);
            }

            if (entryType == null)
            {
                if (candidate.IsBare && entryError == null)
                {
                    Logger.Debug("Ignoring " + candidate.AssemblyPath + " (no Mod subclass, treated as a library)");
                    return null;
                }
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = assembly.GetName().Name.ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, entryError ?? "no class deriving from DnWModLoader.Mod found in " + Path.GetFileName(candidate.AssemblyPath));
            }
            container.EntryType = entryType;

            ModInfoAttribute attribute;
            try
            {
                attribute = entryType.GetCustomAttribute<ModInfoAttribute>();
            }
            catch (Exception e)
            {
                container.Info = new ModInfo(candidate.Manifest ?? new ModManifest { Id = assembly.GetName().Name.ToLowerInvariant() }, candidate.Directory, candidate.AssemblyPath);
                return Fail(container, "could not read the attributes of " + entryType.FullName + ": " + ModLogger.Brief(e), e);
            }
            var effective = candidate.Manifest ?? new ModManifest();
            if (string.IsNullOrEmpty(effective.Id)) effective.Id = attribute?.Id;
            if (string.IsNullOrEmpty(effective.Id)) effective.Id = SanitizeId(assembly.GetName().Name);
            if (string.IsNullOrEmpty(effective.Name)) effective.Name = attribute?.Name ?? entryType.Name;
            if (string.IsNullOrEmpty(effective.Version)) effective.Version = attribute?.Version ?? assembly.GetName().Version?.ToString() ?? "0.0";
            if (string.IsNullOrEmpty(effective.Author)) effective.Author = attribute?.Author;
            if (string.IsNullOrEmpty(effective.Description)) effective.Description = attribute?.Description;

            if (!ModManifest.IsValidId(effective.Id))
            {
                effective.Id = SanitizeId(effective.Id);
                if (!ModManifest.IsValidId(effective.Id)) effective.Id = SanitizeId(assembly.GetName().Name);
            }

            container.Info = new ModInfo(effective, candidate.Directory, candidate.AssemblyPath);
            return container;
        }

        private static ModContainer Fail(ModContainer container, string error, Exception exception = null)
        {
            container.MarkFailed(error, exception);
            return container;
        }

        private static ModInfo FallbackInfo(Candidate candidate)
        {
            var manifest = candidate.Manifest != null && !string.IsNullOrEmpty(candidate.Manifest.Id)
                ? candidate.Manifest
                : new ModManifest { Id = SanitizeId(Path.GetFileNameWithoutExtension(candidate.AssemblyPath ?? candidate.Directory)) };
            return new ModInfo(manifest, candidate.Directory, candidate.AssemblyPath);
        }

        private static string LoadedFrom(Assembly assembly)
        {
            try { return assembly.Location; }
            catch { return null; }
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string SanitizeId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "unnamed";
            var chars = raw.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-' ? ch : '-').ToArray();
            string id = new string(chars).Trim('-', '.', '_');
            return string.IsNullOrEmpty(id) ? "unnamed" : id;
        }

        private static Type FindEntryType(Assembly assembly, string requestedName, out string error)
        {
            error = null;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); Logger.Warning("Some types in " + assembly.GetName().Name + " could not be loaded: " + ModLogger.Describe(e)); }

            if (!string.IsNullOrEmpty(requestedName))
            {
                var requested = types.FirstOrDefault(t => t.FullName == requestedName || t.Name == requestedName);
                if (requested == null) { error = "entry type " + requestedName + " not found in assembly"; return null; }
                if (requested.IsAbstract || !typeof(Mod).IsAssignableFrom(requested)) { error = "entry type " + requestedName + " does not derive from DnWModLoader.Mod"; return null; }
                return requested;
            }

            var entries = types.Where(t => !t.IsAbstract && typeof(Mod).IsAssignableFrom(t)).ToList();
            if (entries.Count == 0) return null;
            if (entries.Count == 1) return entries[0];
            error = "several classes derive from DnWModLoader.Mod (" + string.Join(", ", entries.Select(t => t.FullName)) + "); set \"entryType\" in mod.json";
            return null;
        }

        private static List<ModContainer> OrderForLoading(List<ModContainer> loadable)
        {
            var byId = loadable.ToDictionary(c => c.Info.Id, c => c, StringComparer.OrdinalIgnoreCase);
            var incoming = loadable.ToDictionary(c => c.Info.Id, c => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            var outgoing = loadable.ToDictionary(c => c.Info.Id, c => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

            void Edge(string before, string after)
            {
                if (!byId.ContainsKey(before) || !byId.ContainsKey(after) || string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) return;
                if (outgoing[before].Add(after)) incoming[after].Add(before);
            }

            foreach (var c in loadable)
            {
                var m = c.Info.Manifest;
                foreach (var dep in m.Dependencies) if (dep != null && !string.IsNullOrEmpty(dep.Id)) Edge(dep.Id, c.Info.Id);
                foreach (var id in m.LoadAfter) if (!string.IsNullOrEmpty(id)) Edge(id, c.Info.Id);
                foreach (var id in m.LoadBefore) if (!string.IsNullOrEmpty(id)) Edge(c.Info.Id, id);
            }

            var ready = new SortedSet<string>(loadable.Where(c => incoming[c.Info.Id].Count == 0).Select(c => c.Info.Id), StringComparer.OrdinalIgnoreCase);
            var ordered = new List<ModContainer>();
            while (ready.Count > 0)
            {
                string id = ready.Min;
                ready.Remove(id);
                ordered.Add(byId[id]);
                foreach (var next in outgoing[id])
                {
                    incoming[next].Remove(id);
                    if (incoming[next].Count == 0) ready.Add(next);
                }
            }

            if (ordered.Count != loadable.Count)
            {
                foreach (var c in loadable.Where(c => !ordered.Contains(c)))
                {
                    c.MarkSkipped("dependency / load-order cycle involving " + string.Join(", ", incoming[c.Info.Id]));
                    Logger.Error("Mod " + c.Info.Id + " is part of a load-order cycle and will not be loaded.");
                }
            }
            return ordered;
        }

        private static void InitializeMod(ModContainer container)
        {
            if (container.Status != ModStatus.Discovered) return;
            var info = container.Info;
            var manifest = info.Manifest;

            if (!string.IsNullOrEmpty(manifest.LoaderVersion))
            {
                if (!VersionConstraint.TryParse(manifest.LoaderVersion, out var constraint))
                {
                    Logger.Warning("Mod " + info.Id + " has an unparsable loaderVersion \"" + manifest.LoaderVersion + "\"; ignoring it.");
                }
                else if (!constraint.Satisfies(ParsedVersion))
                {
                    container.MarkSkipped("requires loader " + manifest.LoaderVersion + " but " + Version + " is installed");
                    return;
                }
            }

            foreach (var dep in manifest.Dependencies)
            {
                if (dep == null || string.IsNullOrEmpty(dep.Id)) continue;
                if (!ModsById.TryGetValue(dep.Id, out var target))
                {
                    if (dep.Optional) continue;
                    container.MarkSkipped("missing dependency " + dep);
                    return;
                }
                if (target.Status != ModStatus.Loaded)
                {
                    if (dep.Optional) continue;
                    container.MarkSkipped("dependency " + dep.Id + " is " + target.Status.ToString().ToLowerInvariant() + (string.IsNullOrEmpty(target.Error) ? "" : " (" + target.Error + ")"));
                    return;
                }
                if (!string.IsNullOrEmpty(dep.Version))
                {
                    if (!VersionConstraint.TryParse(dep.Version, out var constraint))
                    {
                        Logger.Warning("Mod " + info.Id + ": unparsable version constraint \"" + dep.Version + "\" for dependency " + dep.Id + "; ignoring it.");
                    }
                    else if (!constraint.Satisfies(target.Info.Version))
                    {
                        container.MarkSkipped("dependency " + dep.Id + " " + dep.Version + " required, but " + target.Info.VersionString + " is installed");
                        return;
                    }
                }
            }

            var stopwatch = Stopwatch.StartNew();
            Mod instance = null;
            try
            {
                Logger.Debug("Initializing " + info + " from " + info.AssemblyPath);
                instance = (Mod)Activator.CreateInstance(container.EntryType);
                instance.Info = info;
                instance.Logger = new ModLogger(info.Id);
                instance.Config = new ModConfig(Path.Combine(ConfigDirectory, info.Id + ".json"), instance.Logger);
                instance.ResourceFolders = ResourceFolder.CreateAll(container, instance.Logger);
                container.Instance = instance;
                container.Settings = instance.Config;

                instance.OnInitialize();

                if (instance.AutoPatch)
                {
                    instance.Harmony.PatchAll(container.Assembly);
                }
                container.PatchedMethodCount = instance.Harmony.GetPatchedMethods().Count();
                container.Status = ModStatus.Loaded;
                container.Error = null;
                ResourceWatcher.Register(instance.ResourceFolders);
            }
            catch (Exception e)
            {
                container.MarkFailed(ModLogger.Brief(e), e);
                Logger.Exception(e, "Mod " + info.Id + " failed to initialize");
                if (instance != null)
                {
                    try { instance.Harmony.UnpatchSelf(); }
                    catch (Exception unpatchError) { Logger.Debug("Unpatching " + info.Id + " failed: " + unpatchError.Message); }
                }
            }
            finally
            {
                container.InitializeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            }
        }
    }
}
