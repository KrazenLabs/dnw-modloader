using System;
using System.Globalization;
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
            catch (Exception e) { EmergencyLog("Bootstrap.Init", e); }
        }

        private static bool _afterRegistrationCalled;

        // BepInEx plugins and MelonLoader mods
        public static void AfterRegistration()
        {
            if (_afterRegistrationCalled || !_initCalled) return;
            _afterRegistrationCalled = true;
            try { ModLoader.AfterRegistration(); }
            catch (Exception e) { EmergencyLog("Bootstrap.AfterRegistration", e); }
        }

        private static void EmergencyLog(string step, Exception e)
        {
            string text = "[DnW] " + step + " failed: " + e;
            try { ModLoader.Logger.Fatal(step + " failed: " + e); } catch { }
            try { UnityEngine.Debug.LogError(text); } catch { }

            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] " + text + Environment.NewLine;
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
