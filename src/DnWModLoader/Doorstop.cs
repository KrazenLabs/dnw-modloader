using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Doorstop
{
    internal static class Entrypoint
    {
        public static void Start()
        {
            DnWModLoader.Preloader.Run();
        }
    }
}

namespace DnWModLoader
{
    internal static class Preloader
    {
        private static readonly List<string> EarlyLog = new List<string>();
        private static readonly object Sync = new object();

        // True when the preloader ran and in-memory patch succeeded
        public static bool Active { get; private set; }
        // True when Doorstop invoked the entry point (whether or not patching succeeded)
        public static bool Invoked { get; private set; }
        public static string Status { get; private set; } = "not started by Doorstop";
        // True when Bootstrap.AfterRegistration was hooked
        public static bool AfterRegistrationHooked { get; private set; }
        public static string AfterRegistrationPhase { get; private set; }
        public static string LoaderDirectory { get; private set; }
        public static string GameDirectory { get; private set; }
        public static string ManagedDirectory { get; private set; }

        public static string[] TakeEarlyLog()
        {
            lock (Sync)
            {
                var lines = EarlyLog.ToArray();
                EarlyLog.Clear();
                return lines;
            }
        }

        public static void Run()
        {
            Invoked = true;
            try
            {
                string dllPath = Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH");
                if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath)) dllPath = typeof(Preloader).Assembly.Location;
                LoaderDirectory = Path.GetDirectoryName(Path.GetFullPath(dllPath));

                string processPath = Environment.GetEnvironmentVariable("DOORSTOP_PROCESS_PATH");
                GameDirectory = !string.IsNullOrEmpty(processPath) ? Path.GetDirectoryName(Path.GetFullPath(processPath)) : GuessGameDirectory(LoaderDirectory);

                ManagedDirectory = Environment.GetEnvironmentVariable("DOORSTOP_MANAGED_FOLDER_DIR");
                if (string.IsNullOrEmpty(ManagedDirectory) || !Directory.Exists(ManagedDirectory)) ManagedDirectory = FindManagedDirectory(GameDirectory);

                Note("Doorstop entry: loader=" + dllPath + " game=" + GameDirectory + " managed=" + ManagedDirectory);
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromLoaderDirectory;

                if (ManagedDirectory == null) throw new DirectoryNotFoundException("Managed folder not found under " + GameDirectory);
                string gameAssembly = Path.Combine(ManagedDirectory, "Assembly-CSharp.dll");
                if (!File.Exists(gameAssembly)) throw new FileNotFoundException("game assembly not found", gameAssembly);

                byte[] patched = AssemblyPatcher.Patch(File.ReadAllBytes(gameAssembly), ManagedDirectory, dllPath, out string target, out string lateTarget, out string latePhase);
                Assembly.Load(patched);
                Active = true;
                AfterRegistrationHooked = lateTarget != null;
                AfterRegistrationPhase = latePhase;
                Status = "in-memory patch of Assembly-CSharp.dll, hooked " + target + (lateTarget != null ? " and " + lateTarget : "");
                Note("Preloaded patched Assembly-CSharp.dll (" + patched.Length + " bytes), hooked " + target + (lateTarget != null ? " and " + lateTarget + " (" + latePhase + ")" : "") + ".");
            }
            catch (Exception e)
            {
                Status = "failed: " + e.GetType().Name + ": " + e.Message;
                Note("Preloader failed: " + e);
                WriteFailureLog(e);
            }
        }

        private static Assembly ResolveFromLoaderDirectory(object sender, ResolveEventArgs args)
        {
            try
            {
                string name = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(name) || name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;

                if (LoaderDirectory != null)
                {
                    string ours = Path.Combine(LoaderDirectory, name + ".dll");
                    if (File.Exists(ours)) return Assembly.LoadFrom(ours);
                }
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return loaded; }
                    catch { }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        internal static string GuessGameDirectory(string startDirectory)
        {
            string dir = startDirectory;
            for (int i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
            {
                if (LooksLikeGameDirectory(dir)) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return startDirectory;
        }

        private static bool LooksLikeGameDirectory(string dir)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, "UnityPlayer.dll"))) return true;
                foreach (var sub in Directory.GetDirectories(dir, "*_Data"))
                    if (Directory.Exists(Path.Combine(sub, "Managed"))) return true;
            }
            catch { }
            return false;
        }

        internal static string FindManagedDirectory(string gameDirectory)
        {
            try
            {
                if (string.IsNullOrEmpty(gameDirectory)) return null;
                foreach (var sub in Directory.GetDirectories(gameDirectory, "*_Data"))
                {
                    string managed = Path.Combine(sub, "Managed");
                    if (Directory.Exists(managed)) return managed;
                }
            }
            catch { }
            return null;
        }

        private static void Note(string message)
        {
            lock (Sync) EarlyLog.Add(message);
        }

        private static void WriteFailureLog(Exception e)
        {
            try
            {
                string path = Path.Combine(LoaderDirectory ?? ".", "preloader-error.log");
                File.WriteAllText(path, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] DnW Mod Loader preloader failed.\r\n" + e + "\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }
}
