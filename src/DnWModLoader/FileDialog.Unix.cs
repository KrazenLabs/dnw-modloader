using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DnWModLoader
{
    internal static partial class FileDialog
    {
        private const int UnixDialogAccepted = 0;
        private const int UnixDialogCancelled = 1;
        private const string AnyFilePattern = "*";

        private static string[] PickUnix(bool folders, string title, string filterName, string filterPatterns)
        {
            string zenity = Platform.FindProgram("zenity");
            string kdialog = Platform.FindProgram("kdialog");
            bool kdeDesktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").IndexOf("KDE", StringComparison.OrdinalIgnoreCase) >= 0;

            if (kdialog != null && (kdeDesktop || zenity == null)) return RunUnixDialog(kdialog, KdialogArguments(folders, title, filterName, filterPatterns));
            if (zenity != null) return RunUnixDialog(zenity, ZenityArguments(folders, title, filterName, filterPatterns));
            throw new InvalidOperationException("no file dialog program found (install zenity or kdialog)");
        }

        internal static List<string> ZenityArguments(bool folders, string title, string filterName, string filterPatterns)
        {
            var arguments = new List<string> { "--file-selection", "--multiple", "--separator=\n" };
            if (!string.IsNullOrEmpty(title)) arguments.Add("--title=" + title);
            if (folders) arguments.Add("--directory");
            else arguments.Add("--file-filter=" + (filterName ?? filterPatterns) + " | " + string.Join(" ", UnixPatterns(filterPatterns).ToArray()));
            return arguments;
        }

        internal static List<string> KdialogArguments(bool folders, string title, string filterName, string filterPatterns)
        {
            var arguments = new List<string>();
            if (!string.IsNullOrEmpty(title)) arguments.Add("--title=" + title);
            if (folders)
            {
                arguments.Add("--getexistingdirectory");
                arguments.Add(StartDirectory());
                return arguments;
            }
            arguments.Add("--getopenfilename");
            arguments.Add(StartDirectory());
            arguments.Add(string.Join(" ", UnixPatterns(filterPatterns).ToArray()) + "|" + (filterName ?? filterPatterns));
            arguments.Add("--multiple");
            arguments.Add("--separate-output");
            return arguments;
        }

        internal static List<string> UnixPatterns(string filterPatterns)
        {
            var patterns = new List<string>();
            if (string.IsNullOrEmpty(filterPatterns)) return new List<string> { AnyFilePattern };
            foreach (string raw in filterPatterns.Split(';'))
            {
                string pattern = raw.Trim();
                if (pattern.Length == 0) continue;
                if (pattern == "*.*") return new List<string> { AnyFilePattern };
                AddOnce(patterns, pattern.ToLowerInvariant());
                AddOnce(patterns, pattern.ToUpperInvariant());
            }
            return patterns.Count == 0 ? new List<string> { AnyFilePattern } : patterns;
        }

        private static void AddOnce(List<string> list, string value)
        {
            if (!list.Contains(value)) list.Add(value);
        }

        private static string StartDirectory()
        {
            string home = Environment.GetEnvironmentVariable("HOME");
            return string.IsNullOrEmpty(home) ? Environment.GetFolderPath(Environment.SpecialFolder.Personal) : home;
        }

        private static string[] RunUnixDialog(string program, List<string> arguments)
        {
            var info = Platform.UnixStartInfo(program, arguments);
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            var errors = new StringBuilder();
            string output;
            int exitCode;
            using (var process = new Process { StartInfo = info })
            {
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null) return;
                    lock (errors)
                        if (errors.Length < 1000) errors.AppendLine(e.Data);
                };
                process.Start();
                process.BeginErrorReadLine();
                output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            if (exitCode == UnixDialogCancelled) return new string[0];
            if (exitCode != UnixDialogAccepted)
            {
                string detail;
                lock (errors) detail = errors.ToString().Trim();
                throw new InvalidOperationException(System.IO.Path.GetFileName(program) + " failed with exit code " + exitCode + (detail.Length > 0 ? ": " + FirstLine(detail) : ""));
            }
            return ParseUnixSelection(output);
        }

        internal static string[] ParseUnixSelection(string output)
        {
            var paths = new List<string>();
            foreach (string line in (output ?? "").Split('\n'))
            {
                string path = line.TrimEnd('\r');
                if (path.Length > 0) paths.Add(path);
            }
            return paths.ToArray();
        }

        private static string FirstLine(string text)
        {
            int end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end).TrimEnd('\r');
        }
    }
}
