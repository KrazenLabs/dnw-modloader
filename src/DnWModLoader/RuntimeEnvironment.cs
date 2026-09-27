using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DnWModLoader
{
    internal static class RuntimeEnvironment
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr GetModuleHandleW(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr WineGetVersion();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void WineGetHostVersion(out IntPtr system, out IntPtr release);

        internal static string Describe()
        {
            var parts = new List<string>();
            bool linux = !Platform.IsWindows;
            if (Platform.IsWindows)
            {
                string wine = WineVersion();
                linux = wine != null;
                parts.Add(wine ?? "Windows");
            }
            else
            {
                parts.Add("native Linux");
            }

            string app = Variable("SteamAppId") ?? Variable("SteamGameId");
            parts.Add(app != null ? "started by Steam (app " + app + ")" : "not started by Steam");

            if (linux)
            {
                if (Variable("container") == "pressure-vessel")
                    parts.Add("Steam Linux Runtime container" + (Variable("PRESSURE_VESSEL_RUNTIME") is string runtime ? " (" + runtime + ")" : ""));
                else if (Variable("STEAM_RUNTIME") is string scout && scout != "0")
                    parts.Add("Steam Runtime libraries, no container");
                parts.Add(Session());
                if (Variable("SteamDeck") == "1") parts.Add("Steam Deck");
            }
            return string.Join(" | ", parts.ToArray());
        }

        private static string WineVersion()
        {
            try
            {
                IntPtr ntdll = GetModuleHandleW("ntdll.dll");
                if (ntdll == IntPtr.Zero) return null;
                IntPtr getVersion = GetProcAddress(ntdll, "wine_get_version");
                if (getVersion == IntPtr.Zero) return null;

                var version = (WineGetVersion)Marshal.GetDelegateForFunctionPointer(getVersion, typeof(WineGetVersion));
                string text = "Wine " + Marshal.PtrToStringAnsi(version());
                string proton = ProtonName();
                if (proton != null) text += " (" + proton + ")";

                IntPtr getHost = GetProcAddress(ntdll, "wine_get_host_version");
                if (getHost != IntPtr.Zero)
                {
                    IntPtr system, release;
                    ((WineGetHostVersion)Marshal.GetDelegateForFunctionPointer(getHost, typeof(WineGetHostVersion)))(out system, out release);
                    text += " on " + Marshal.PtrToStringAnsi(system) + " " + Marshal.PtrToStringAnsi(release);
                }
                return text;
            }
            catch
            {
                return null;
            }
        }

        private static string ProtonName()
        {
            string tools = Variable("STEAM_COMPAT_TOOL_PATHS");
            if (tools == null) return null;
            string tool = tools.Split(':')[0].TrimEnd('/');
            if (tool.Length == 0) return null;
            try
            {
                string versionFile = "Z:" + tool.Replace('/', '\\') + "\\version";
                if (File.Exists(versionFile))
                {
                    string[] words = File.ReadAllText(versionFile).Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 1) return words[1];
                }
            }
            catch
            {
            }
            int slash = tool.LastIndexOf('/');
            return slash >= 0 ? tool.Substring(slash + 1) : tool;
        }

        private static string Session()
        {
            string desktop = Variable("XDG_CURRENT_DESKTOP");
            if (string.Equals(desktop, "gamescope", StringComparison.OrdinalIgnoreCase) || Variable("GAMESCOPE_WAYLAND_DISPLAY") != null)
                return "Game Mode (gamescope)";
            string type = Variable("XDG_SESSION_TYPE");
            if (type == null || type == "tty" || type == "unspecified")
                type = Variable("WAYLAND_DISPLAY") != null ? "wayland" : Variable("DISPLAY") != null ? "x11" : null;
            string display = type == "x11" ? "X11" : type == "wayland" ? "Wayland" : type ?? "no display";
            return "session " + (desktop == null ? display : desktop + ", " + display);
        }

        private static string Variable(string name)
        {
            try
            {
                string value = Environment.GetEnvironmentVariable(name);
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }
    }
}
