using System;
using System.IO;

namespace DnWModLoader
{
    public static class Bootstrap
    {
        private static bool _initCalled;

        // Loads and initializes all mods
        public static void Init()
        {
            if (_initCalled) return;
            _initCalled = true;
            try { ModLoader.Initialize(); }
            catch (Exception e) { EmergencyLog(e); }
        }

        private static bool _afterRegistrationCalled;

        // BepInEx plugins
        public static void AfterRegistration()
        {
            if (_afterRegistrationCalled || !_initCalled) return;
            _afterRegistrationCalled = true;
            try { ModLoader.AfterRegistration(); }
            catch (Exception e) { EmergencyLog(e); }
        }

        private static void EmergencyLog(Exception e)
        {
            string text = "[DnW] Bootstrap.Init failed: " + e;
            try { ModLoader.Logger.Fatal("Bootstrap.Init failed: " + e); } catch { }
            try { UnityEngine.Debug.LogError(text); } catch { }

            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + text + Environment.NewLine;
            if (!AppendCrashLog(() => Path.Combine(ModLoader.GuessGameDirectory(), ModLoader.ModsFolderName), line))
                AppendCrashLog(ModLoader.FallbackDirectory, line);
        }

        private static bool AppendCrashLog(Func<string> directory, string line)
        {
            try
            {
                string folder = directory();
                if (string.IsNullOrEmpty(folder)) return false;
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "ModLoader.crash.log"), line);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
