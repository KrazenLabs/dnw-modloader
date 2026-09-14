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

        private static void EmergencyLog(Exception e)
        {
            string text = "[DnW] Bootstrap.Init failed: " + e;
            try { ModLoader.Logger.Fatal("Bootstrap.Init failed: " + e); } catch { }
            try { UnityEngine.Debug.LogError(text); } catch { }
            try
            {
                string crashLog = Path.Combine(ModLoader.GuessGameDirectory(), ModLoader.ModsFolderName, "ModLoader.crash.log");
                Directory.CreateDirectory(Path.GetDirectoryName(crashLog));
                File.AppendAllText(crashLog, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + text + Environment.NewLine);
            }
            catch { }
        }
    }
}
