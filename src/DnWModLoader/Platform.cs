using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DnWModLoader
{
    internal static class Platform
    {
        private const string DoorstopVariablePrefix = "DOORSTOP_";
        private const string DoorstopLibraryPrefix = "libdoorstop";
        private static readonly string[] PreloadVariables = { "LD_PRELOAD", "DYLD_INSERT_LIBRARIES" };

        internal static readonly bool IsWindows = Path.DirectorySeparatorChar == '\\';

        internal static readonly StringComparison PathComparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        internal static readonly StringComparer PathComparer = IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        internal static void ShowFolder(string folder)
        {
            if (!IsWindows)
            {
                OpenWithDesktop(folder);
                return;
            }
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string explorer = string.IsNullOrEmpty(windows) ? "explorer.exe" : Path.Combine(windows, "explorer.exe");
            using (Process.Start(new ProcessStartInfo(explorer, "\"" + folder + "\"") { UseShellExecute = false })) { }
        }

        internal static void OpenPath(string path)
        {
            if (!IsWindows)
            {
                OpenWithDesktop(path);
                return;
            }
            using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { }
        }

        internal static string FindProgram(string name)
        {
            string search = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(search)) return null;
            foreach (string directory in search.Split(Path.PathSeparator))
            {
                if (directory.Length == 0) continue;
                string candidate;
                try { candidate = Path.Combine(directory, name); }
                catch (ArgumentException) { continue; }
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        internal static ProcessStartInfo UnixStartInfo(string program, IEnumerable<string> arguments)
        {
            var text = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(QuoteUnixArgument(argument));
            }
            var info = new ProcessStartInfo(program, text.ToString()) { UseShellExecute = false };
            RemoveLoaderEnvironment(info);
            return info;
        }

        internal static string QuoteUnixArgument(string argument)
        {
            return "'" + (argument ?? "").Replace("'", "'\\''") + "'";
        }

        internal static void RemoveLoaderEnvironment(ProcessStartInfo info)
        {
            var variables = info.EnvironmentVariables;
            var doorstopKeys = new List<string>();
            foreach (string key in variables.Keys)
                if (key != null && key.StartsWith(DoorstopVariablePrefix, StringComparison.Ordinal)) doorstopKeys.Add(key);
            foreach (string key in doorstopKeys) variables.Remove(key);

            foreach (string name in PreloadVariables)
            {
                string value = variables[name];
                if (string.IsNullOrEmpty(value)) continue;
                var kept = new List<string>();
                foreach (string library in value.Split(':', ' '))
                {
                    if (library.Length == 0) continue;
                    if (Path.GetFileName(library).StartsWith(DoorstopLibraryPrefix, StringComparison.Ordinal)) continue;
                    kept.Add(library);
                }
                if (kept.Count == 0) variables.Remove(name);
                else variables[name] = string.Join(":", kept.ToArray());
            }
        }

        internal static string MatchCase(string directory, string relativePath)
        {
            string exact = Path.Combine(directory, relativePath);
            if (IsWindows || File.Exists(exact) || Directory.Exists(exact)) return exact;

            string current = directory;
            string[] parts = relativePath.Split('/', '\\');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part == ".") continue;
                string next = Path.Combine(current, part);
                if (!File.Exists(next) && !Directory.Exists(next))
                {
                    string found = FindEntryIgnoringCase(current, part);
                    if (found == null) return exact;
                    next = found;
                }
                current = next;
            }
            return current;
        }

        private static string FindEntryIgnoringCase(string directory, string name)
        {
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory); }
            catch (Exception) { return null; }
            string match = null;
            foreach (string entry in entries)
            {
                if (!string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase)) continue;
                if (match != null) return null;
                match = entry;
            }
            return match;
        }

        private static void OpenWithDesktop(string path)
        {
            string opener = FindProgram("xdg-open");
            if (opener == null) throw new FileNotFoundException("xdg-open was not found");
            using (Process.Start(UnixStartInfo(opener, new[] { path }))) { }
        }
    }
}
