using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BepInEx
{
    public abstract class BaseUnityPlugin : MonoBehaviour
    {
        public PluginInfo Info { get; }

        protected ManualLogSource Logger { get; }

        public ConfigFile Config { get; }

        protected BaseUnityPlugin()
        {
            var type = GetType();
            var metadata = MetadataHelper.GetMetadata(type);
            if (metadata == null)
                throw new InvalidOperationException("Can't create an instance of " + type.FullName + " because it inherits from BaseUnityPlugin and the BepInPlugin attribute is missing.");

            if (Chainloader.PluginInfos.TryGetValue(metadata.GUID, out var info))
            {
                Info = info;
            }
            else
            {
                // Created outside the loader, e.g. by another plugin
                Info = new PluginInfo
                {
                    Metadata = metadata,
                    Instance = this,
                    Dependencies = MetadataHelper.GetDependencies(type),
                    Processes = MetadataHelper.GetAttributes<BepInProcess>(type),
                    Incompatibilities = MetadataHelper.GetAttributes<BepInIncompatibility>(type),
                    Location = type.Assembly.Location,
                    TypeName = type.FullName,
                };
            }

            Logger = Logging.Logger.CreateLogSource(metadata.Name);
            Paths.EnsureInitialized();
            Config = new ConfigFile(Path.Combine(Paths.ConfigPath, metadata.GUID + ".cfg"), false, metadata);
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class BepInPlugin : Attribute
    {
        public string GUID { get; protected set; }
        public string Name { get; protected set; }
        public Version Version { get; protected set; }

        public BepInPlugin(string GUID, string Name, string Version)
        {
            this.GUID = GUID;
            this.Name = Name;
            this.Version = ParseVersion(Version);
        }

        // null when the text is not a valid version
        internal static Version ParseVersion(string text)
        {
            try { return new Version(text); }
            catch { return null; }
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInDependency : Attribute, ICacheable
    {
        [Flags]
        public enum DependencyFlags
        {
            HardDependency = 1,
            SoftDependency = 2,
        }

        public string DependencyGUID { get; protected set; }
        public DependencyFlags Flags { get; protected set; }
        public Version MinimumVersion { get; protected set; }

        public BepInDependency(string DependencyGUID, DependencyFlags Flags = DependencyFlags.HardDependency)
        {
            this.DependencyGUID = DependencyGUID;
            this.Flags = Flags;
            MinimumVersion = new Version();
        }

        public BepInDependency(string DependencyGUID, string MinimumDependencyVersion) : this(DependencyGUID)
        {
            MinimumVersion = new Version(MinimumDependencyVersion);
        }

        void ICacheable.Save(BinaryWriter bw)
        {
            bw.Write(DependencyGUID);
            bw.Write((int)Flags);
            bw.Write(MinimumVersion.ToString());
        }

        void ICacheable.Load(BinaryReader br)
        {
            DependencyGUID = br.ReadString();
            Flags = (DependencyFlags)br.ReadInt32();
            MinimumVersion = new Version(br.ReadString());
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInIncompatibility : Attribute, ICacheable
    {
        public string IncompatibilityGUID { get; protected set; }

        public BepInIncompatibility(string IncompatibilityGUID)
        {
            this.IncompatibilityGUID = IncompatibilityGUID;
        }

        void ICacheable.Save(BinaryWriter bw) { bw.Write(IncompatibilityGUID); }

        void ICacheable.Load(BinaryReader br) { IncompatibilityGUID = br.ReadString(); }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInProcess : Attribute
    {
        public string ProcessName { get; protected set; }

        public BepInProcess(string ProcessName)
        {
            this.ProcessName = ProcessName;
        }
    }

    public class PluginInfo : ICacheable
    {
        public BepInPlugin Metadata { get; internal set; }
        public IEnumerable<BepInProcess> Processes { get; internal set; } = new BepInProcess[0];
        public IEnumerable<BepInDependency> Dependencies { get; internal set; } = new BepInDependency[0];
        public IEnumerable<BepInIncompatibility> Incompatibilities { get; internal set; } = new BepInIncompatibility[0];
        public string Location { get; internal set; }
        public BaseUnityPlugin Instance { get; internal set; }

        internal string TypeName { get; set; }
        internal Version TargettedBepInExVersion { get; set; }

        void ICacheable.Save(BinaryWriter bw)
        {
            bw.Write(TypeName ?? "");
            bw.Write(Metadata.GUID);
            bw.Write(Metadata.Name);
            bw.Write(Metadata.Version.ToString());
            var processes = Processes.ToList();
            bw.Write(processes.Count);
            foreach (var process in processes) bw.Write(process.ProcessName);
            var dependencies = Dependencies.ToList();
            bw.Write(dependencies.Count);
            foreach (ICacheable dependency in dependencies) dependency.Save(bw);
            var incompatibilities = Incompatibilities.ToList();
            bw.Write(incompatibilities.Count);
            foreach (ICacheable incompatibility in incompatibilities) incompatibility.Save(bw);
            bw.Write((TargettedBepInExVersion ?? new Version(0, 0, 0, 0)).ToString(4));
        }

        void ICacheable.Load(BinaryReader br)
        {
            TypeName = br.ReadString();
            Metadata = new BepInPlugin(br.ReadString(), br.ReadString(), br.ReadString());
            int count = br.ReadInt32();
            var processes = new List<BepInProcess>(count);
            for (int i = 0; i < count; i++) processes.Add(new BepInProcess(br.ReadString()));
            Processes = processes;
            count = br.ReadInt32();
            var dependencies = new List<BepInDependency>(count);
            for (int i = 0; i < count; i++)
            {
                var dependency = new BepInDependency("");
                ((ICacheable)dependency).Load(br);
                dependencies.Add(dependency);
            }
            Dependencies = dependencies;
            count = br.ReadInt32();
            var incompatibilities = new List<BepInIncompatibility>(count);
            for (int i = 0; i < count; i++)
            {
                var incompatibility = new BepInIncompatibility("");
                ((ICacheable)incompatibility).Load(br);
                incompatibilities.Add(incompatibility);
            }
            Incompatibilities = incompatibilities;
            TargettedBepInExVersion = new Version(br.ReadString());
        }

        public override string ToString()
        {
            return Metadata?.Name + " " + Metadata?.Version;
        }
    }

    public static class MetadataHelper
    {
        public static BepInPlugin GetMetadata(Type pluginType)
        {
            var attributes = pluginType.GetCustomAttributes(typeof(BepInPlugin), false);
            return attributes.Length == 0 ? null : (BepInPlugin)attributes[0];
        }

        public static BepInPlugin GetMetadata(object plugin)
        {
            return GetMetadata(plugin.GetType());
        }

        public static T[] GetAttributes<T>(Type pluginType) where T : Attribute
        {
            return (T[])pluginType.GetCustomAttributes(typeof(T), true);
        }

        public static IEnumerable<T> GetAttributes<T>(object plugin) where T : Attribute
        {
            return GetAttributes<T>(plugin.GetType());
        }

        public static IEnumerable<BepInDependency> GetDependencies(Type plugin)
        {
            return plugin.GetCustomAttributes(typeof(BepInDependency), true).Cast<BepInDependency>();
        }
    }

    public static class Paths
    {
        public static string[] DllSearchPaths { get; private set; }
        public static string BepInExAssemblyDirectory { get; private set; }
        public static string BepInExAssemblyPath { get; private set; }
        public static string BepInExRootPath { get; private set; }
        public static string ExecutablePath { get; private set; }
        public static string GameRootPath { get; private set; }
        public static string ManagedPath { get; private set; }
        public static string ConfigPath { get; private set; }
        public static string BepInExConfigPath { get; private set; }
        public static string CachePath { get; private set; }
        public static string PatcherPluginPath { get; private set; }
        public static string PluginPath { get; private set; }
        public static string ProcessName { get; private set; }

        // Calls before plugin creation
        internal static void Initialize(string executablePath, string gameRoot, string managedPath, string compatAssemblyPath)
        {
            ExecutablePath = executablePath;
            ProcessName = Path.GetFileNameWithoutExtension(executablePath);
            GameRootPath = gameRoot;
            ManagedPath = managedPath;
            BepInExRootPath = Path.Combine(gameRoot, "BepInEx");
            ConfigPath = Path.Combine(BepInExRootPath, "config");
            BepInExConfigPath = Path.Combine(ConfigPath, "BepInEx.cfg");
            PluginPath = Path.Combine(BepInExRootPath, "plugins");
            PatcherPluginPath = Path.Combine(BepInExRootPath, "patchers");
            CachePath = Path.Combine(BepInExRootPath, "cache");
            BepInExAssemblyPath = compatAssemblyPath;
            BepInExAssemblyDirectory = Path.GetDirectoryName(compatAssemblyPath);
            DllSearchPaths = new[] { managedPath };
        }

        internal static void EnsureInitialized()
        {
            if (ConfigPath != null) return;
            string data = Application.dataPath;
            string game = Path.GetDirectoryName(data);
            string exe = Path.Combine(game, Path.GetFileNameWithoutExtension(data).Replace("_Data", "") + ".exe");
            Initialize(exe, game, Path.Combine(data, "Managed"), typeof(Paths).Assembly.Location);
        }
    }
}

namespace BepInEx.Bootstrap
{
    public interface ICacheable
    {
        void Save(BinaryWriter bw);
        void Load(BinaryReader br);
    }

    public static class Chainloader
    {
        private static readonly List<BaseUnityPlugin> LoadedPlugins = new List<BaseUnityPlugin>();

        public static Dictionary<string, PluginInfo> PluginInfos { get; } = new Dictionary<string, PluginInfo>();

        [Obsolete("Use PluginInfos instead")]
        public static List<BaseUnityPlugin> Plugins
        {
            get
            {
                lock (LoadedPlugins)
                {
                    LoadedPlugins.RemoveAll(p => p == null);
                    return LoadedPlugins.ToList();
                }
            }
        }

        public static List<string> DependencyErrors { get; } = new List<string>();

        public static GameObject ManagerObject { get; internal set; }

        public static void Initialize(string gameExePath, bool startConsole = true, ICollection<LogEventArgs> preloaderLogEvents = null)
        {
        }

        public static void Start()
        {
        }

        internal static void AddLoaded(BaseUnityPlugin plugin)
        {
            lock (LoadedPlugins) LoadedPlugins.Add(plugin);
        }
    }
}
