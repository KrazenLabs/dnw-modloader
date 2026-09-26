using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace DnWModLoader
{
    internal sealed class ImportResult
    {
        public readonly List<string> Added = new List<string>();
        public int AlreadyThere;
        public int Unsupported;
        public int Failed;
        public string FirstError;
        public bool Truncated;

        public int Total { get { return Added.Count + AlreadyThere + Unsupported + Failed; } }
    }

    internal static class ResourceImport
    {
        internal const int MaxFiles = 2000;
        internal const int MaxScannedEntries = 100000;
        private const int CopyBufferSize = 64 * 1024;
        private const int MaxNameSuffix = 9999;
        private const double MessageSeconds = 10;

        private static volatile bool _busy;

        public static bool Busy { get { return _busy; } }

        public static bool Begin(ResourceFolder folder, bool pickFolders)
        {
            if (folder == null || _busy) return false;
            _busy = true;
            IntPtr owner = FileDialog.ActiveWindow();
            folder.ImportStatus = pickFolders ? "Choose folders..." : "Choose files...";
            try
            {
                var thread = new Thread(() => Run(folder, pickFolders, owner)) { IsBackground = true, Name = "DnW file dialog" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                return true;
            }
            catch (Exception e)
            {
                folder.ImportStatus = null;
                _busy = false;
                OpenInstead(folder, e.Message);
                return false;
            }
        }

        private static void Run(ResourceFolder folder, bool pickFolders, IntPtr owner)
        {
            try
            {
                string[] picked;
                try
                {
                    picked = FileDialog.Pick(owner, pickFolders, Title(folder, pickFolders), FilterName(folder), FilterPatterns(folder));
                }
                catch (Exception e)
                {
                    string reason = e.Message;
                    ResourceWatcher.Post(() => OpenInstead(folder, reason));
                    return;
                }
                if (picked == null || picked.Length == 0) return;

                folder.ImportStatus = "Importing files...";
                ImportResult result;
                try
                {
                    result = Import(folder, picked);
                }
                catch (Exception e)
                {
                    result = new ImportResult { Failed = 1, FirstError = e.Message };
                }
                ResourceWatcher.Poke();
                ResourceWatcher.Post(() => Report(folder, result));
            }
            finally
            {
                folder.ImportStatus = null;
                _busy = false;
            }
        }

        private static void OpenInstead(ResourceFolder folder, string reason)
        {
            ModLoader.Logger.Warning("Failed to open file dialog (" + reason + "); opening " + folder.Path + " instead.");
            folder.ShowImportMessage("Failed to open file dialog, folder was opened instead.", true, MessageSeconds);
            folder.Open();
        }

        private static void Report(ResourceFolder folder, ImportResult result)
        {
            string modName = folder.Owner?.Info != null ? folder.Owner.Info.Name : folder.ModId;
            string summary = Describe(result, folder);
            string line = "Resource folder " + folder.DisplayName + " of " + modName + ": " + summary;
            if (result.Failed > 0) ModLoader.Logger.Warning(line);
            else ModLoader.Logger.Info(line);
            foreach (var path in result.Added) ModLoader.Logger.Debug("Imported " + path);
            folder.ShowImportMessage(summary, result.Failed > 0 && result.Added.Count == 0, MessageSeconds);
        }

        internal static string Title(ResourceFolder folder, bool pickFolders)
        {
            return (pickFolders ? "Import folders to " : "Import files to ") + folder.DisplayName;
        }

        internal static string FilterName(ResourceFolder folder)
        {
            return folder.Extensions.Count == 0 ? "Import files (*.*)" : folder.DisplayName + " (" + FilterPatterns(folder) + ")";
        }

        internal static string FilterPatterns(ResourceFolder folder)
        {
            if (folder.Extensions.Count == 0) return "*.*";
            var patterns = new string[folder.Extensions.Count];
            for (int i = 0; i < patterns.Length; i++) patterns[i] = "*" + folder.Extensions[i];
            return string.Join(";", patterns);
        }

        internal static string Describe(ImportResult result, ResourceFolder folder)
        {
            if (result.Total == 0 && !result.Truncated) return "Nothing to import.";
            bool onlyAlreadyThere = result.Added.Count == 0 && result.AlreadyThere > 0 && result.Unsupported == 0 && result.Failed == 0;
            var parts = new List<string>();
            if (result.Added.Count > 0) parts.Add("Imported " + Files(result.Added.Count) + ".");
            else if (onlyAlreadyThere) parts.Add(result.AlreadyThere == 1 ? "This file already exists." : "Those files already exist.");
            else parts.Add("Nothing imported.");
            if (result.AlreadyThere > 0 && !onlyAlreadyThere) parts.Add(result.AlreadyThere + (result.AlreadyThere == 1 ? " exists" : " exist") + " already.");
            if (result.Unsupported > 0)
                parts.Add(Files(result.Unsupported) + " skipped" + (folder.Extensions.Count == 0 ? " (programs and scripts are not allowed)." : ", only " + folder.FileTypesLabel + " files are allowed."));
            if (result.Failed > 0) parts.Add(Files(result.Failed) + " failed to import: " + result.FirstError);
            if (result.Truncated) parts.Add("Stopped early, too many files.");
            return string.Join(" ", parts.ToArray());
        }

        private static string Files(int count)
        {
            return count == 1 ? "1 file" : count + " files";
        }

        internal static ImportResult Import(ResourceFolder folder, IList<string> sources)
        {
            var result = new ImportResult();
            string error;
            if (!folder.EnsureExists(out error))
            {
                result.Failed = Math.Max(1, sources.Count);
                result.FirstError = error;
                return result;
            }

            var plan = new List<KeyValuePair<string, string>>();
            int scanned = 0;
            foreach (string raw in sources)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                string source;
                try { source = ResourceFolder.TrimSeparators(Path.GetFullPath(raw)); }
                catch (Exception e)
                {
                    Fail(result, raw, e.Message);
                    continue;
                }
                if (Directory.Exists(source)) CollectFolder(folder, source, plan, result, ref scanned);
                else if (File.Exists(source)) Plan(folder, source, Path.GetFileName(source), plan, result);
                else Fail(result, source, "not found");
                if (result.Truncated) break;
            }

            foreach (var item in plan) Copy(folder, item.Key, item.Value, result);
            return result;
        }

        private static void CollectFolder(ResourceFolder folder, string source, List<KeyValuePair<string, string>> plan, ImportResult result, ref int scanned)
        {
            string baseName = Path.GetFileName(source);
            if (string.IsNullOrEmpty(baseName)) baseName = source.TrimEnd('\\', '/', ':');
            string prefix = source.EndsWith("\\", StringComparison.Ordinal) ? source : source + "\\";

            var pending = new Stack<string>();
            pending.Push(source);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                FileInfo[] files;
                DirectoryInfo[] subdirectories;
                try
                {
                    var info = new DirectoryInfo(directory);
                    files = info.GetFiles();
                    subdirectories = info.GetDirectories();
                }
                catch (Exception e)
                {
                    if (result.FirstError == null) result.FirstError = directory + ": " + e.Message;
                    continue;
                }

                Array.Sort(files, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
                foreach (var file in files)
                {
                    if (++scanned > MaxScannedEntries)
                    {
                        result.Truncated = true;
                        return;
                    }
                    if (!Visible(file)) continue;
                    string relative = folder.Recursive ? Path.Combine(baseName, file.FullName.Substring(prefix.Length)) : file.Name;
                    Plan(folder, file.FullName, relative, plan, result);
                    if (result.Truncated) return;
                }

                Array.Sort(subdirectories, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(b.Name, a.Name));
                foreach (var subdirectory in subdirectories)
                {
                    if (++scanned > MaxScannedEntries)
                    {
                        result.Truncated = true;
                        return;
                    }
                    try
                    {
                        var attributes = subdirectory.Attributes;
                        if (ResourceFolder.IsVisible(attributes) && (attributes & FileAttributes.ReparsePoint) == 0) pending.Push(subdirectory.FullName);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static bool Visible(FileInfo file)
        {
            try { return ResourceFolder.IsVisible(file.Attributes); }
            catch (Exception) { return false; }
        }

        private static void Plan(ResourceFolder folder, string source, string relative, List<KeyValuePair<string, string>> plan, ImportResult result)
        {
            if (!folder.Accepts(source))
            {
                result.Unsupported++;
                return;
            }
            if (ResourceFolder.IsInside(folder.Path, source))
            {
                result.AlreadyThere++;
                return;
            }
            if (plan.Count >= MaxFiles)
            {
                result.Truncated = true;
                return;
            }
            plan.Add(new KeyValuePair<string, string>(source, relative));
        }

        private static void Copy(ResourceFolder folder, string source, string relative, ImportResult result)
        {
            string temp = null;
            try
            {
                string root = folder.Path;
                string target = ResourceFolder.TrimSeparators(Path.GetFullPath(Path.Combine(root, relative)));
                if (!ResourceFolder.IsInside(root, target) || string.Equals(root, target, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Folder outside mod root");
                string directory = Path.GetDirectoryName(target);
                if (ResourceFolder.HasLinkBetween(root, directory)) throw new IOException("A folder links outside mod root");
                Directory.CreateDirectory(directory);

                if (File.Exists(target))
                {
                    if (SameContent(source, target))
                    {
                        result.AlreadyThere++;
                        return;
                    }
                    target = UniqueName(target);
                }

                temp = target + ResourceFolder.ImportExtension;
                File.Copy(source, temp, true);
                var attributes = File.GetAttributes(temp);
                if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(temp, attributes & ~FileAttributes.ReadOnly);
                if (File.Exists(target) || Directory.Exists(target)) target = UniqueName(target);
                File.Move(temp, target);
                temp = null;
                result.Added.Add(target);
            }
            catch (Exception e)
            {
                Fail(result, source, e.Message);
            }
            finally
            {
                if (temp != null)
                {
                    try { File.Delete(temp); }
                    catch (Exception) { }
                }
            }
        }

        private static void Fail(ImportResult result, string source, string message)
        {
            result.Failed++;
            if (result.FirstError == null) result.FirstError = Path.GetFileName(source) + ": " + message;
        }

        internal static string UniqueName(string path)
        {
            string directory = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int n = 2; n <= MaxNameSuffix; n++)
            {
                string candidate = Path.Combine(directory, stem + " (" + n + ")" + extension);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
            throw new IOException("too many files named " + stem + extension);
        }

        internal static bool SameContent(string a, string b)
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            using (var first = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var second = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var bufferA = new byte[CopyBufferSize];
                var bufferB = new byte[CopyBufferSize];
                while (true)
                {
                    int readA = ReadFull(first, bufferA);
                    int readB = ReadFull(second, bufferB);
                    if (readA != readB) return false;
                    if (readA == 0) return true;
                    for (int i = 0; i < readA; i++)
                        if (bufferA[i] != bufferB[i]) return false;
                }
            }
        }

        private static int ReadFull(Stream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0) break;
                total += read;
            }
            return total;
        }
    }
}
