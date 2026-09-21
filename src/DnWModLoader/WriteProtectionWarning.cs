using System;
using System.IO;
using DnWModLoader.Logging;

namespace DnWModLoader
{
    internal static class WriteProtectionWarning
    {
        public const string Banner = "The game folder is write-protected. Please open the DnW Mod Manager to fix this.";

        private static bool _checked;
        private static bool _applies;

        public static bool Applies()
        {
            if (!_checked)
            {
                _checked = true;
                try { _applies = Detect(); }
                catch (Exception) { _applies = false; }
            }
            return _applies;
        }

        public static string LogMessage
        {
            get
            {
                return "The game folder is write-protected.";
            }
        }

        private static bool Detect()
        {
            string directory = Directory.Exists(ModLoader.ModsDirectory) ? ModLoader.ModsDirectory : ModLoader.GameDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;

            string probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
    }
}
