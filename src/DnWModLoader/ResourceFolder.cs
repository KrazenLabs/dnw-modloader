using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using DnWModLoader.Logging;
using IOPath = System.IO.Path;

namespace DnWModLoader
{
    public sealed class ResourceChanges
    {
        private static readonly string[] None = new string[0];

        internal ResourceChanges(string[] added, string[] removed, string[] changed)
        {
            Added = added ?? None;
            Removed = removed ?? None;
            Changed = changed ?? None;
        }

        public IReadOnlyList<string> Added { get; }

        public IReadOnlyList<string> Removed { get; }

        public IReadOnlyList<string> Changed { get; }

        public int Count { get { return Added.Count + Removed.Count + Changed.Count; } }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Added.Count > 0) parts.Add(Added.Count + " added");
            if (Removed.Count > 0) parts.Add(Removed.Count + " removed");
            if (Changed.Count > 0) parts.Add(Changed.Count + " changed");
            return parts.Count == 0 ? "no changes" : string.Join(", ", parts.ToArray());
        }
    }

    internal struct FileStamp
    {
        public readonly long Length;
        public readonly long WriteTicks;

        public FileStamp(long length, long writeTicks)
        {
            Length = length;
            WriteTicks = writeTicks;
        }
    }

    public sealed class ResourceFolder
    {
        internal const int MaxFiles = 20000;
        internal const string ImportExtension = ".dnwimport";

        private static readonly string[] NoFiles = new string[0];
        private static readonly ResourceFolder[] NoFolders = new ResourceFolder[0];
        private static readonly char[] Separators = { '\\', '/' };
        private static readonly char[] ForbiddenPathChars = BuildForbiddenPathChars();

        private static readonly HashSet<string> BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
            ".msi", ".msp", ".scr", ".pif", ".lnk", ".url", ".reg", ".cpl", ".hta", ".jar", ".sys", ".ocx", ImportExtension,
        };

        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private readonly HashSet<string> _extensions;
        private IReadOnlyList<string> _files = NoFiles;

        private ResourceFolder(ModContainer owner, string modDirectory, string folder, string path, ResourceFolderDeclaration declaration, string[] extensions)
        {
            Owner = owner;
            ModId = owner?.Info?.Id;
            ModDirectory = modDirectory;
            Folder = folder;
            Path = path;
            DisplayName = !string.IsNullOrEmpty(declaration.Name) && declaration.Name.Trim().Length > 0 ? declaration.Name.Trim() : DefaultName(folder);
            Description = string.IsNullOrEmpty(declaration.Description) ? null : declaration.Description.Trim();
            Section = string.IsNullOrEmpty(declaration.Section) || declaration.Section.Trim().Length == 0 ? null : declaration.Section.Trim();
            Recursive = declaration.Recursive;
            Extensions = extensions;
            _extensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            SetFiles(NoFiles);
        }

        public string ModId { get; }

        public string Folder { get; }

        public string Path { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public string Section { get; }

        public IReadOnlyList<string> Extensions { get; }

        public bool Recursive { get; }

        public IReadOnlyList<string> Files { get { return _files; } }

        internal ModContainer Owner { get; }

        internal string ModDirectory { get; }

        internal string CountLabel { get; private set; }

        internal string FileTypesLabel
        {
            get { return Extensions.Count == 0 ? "any file" : string.Join(", ", ToArray(Extensions)); }
        }

        internal Dictionary<string, FileStamp> Reported;
        internal Dictionary<string, FileStamp> LastScan;
        internal DateTime UnreadableSince;
        internal string LastScanError;

        internal volatile string ImportStatus;
        internal string ImportMessage;
        internal bool ImportMessageIsError;
        internal DateTime ImportMessageUntil;

        public bool Accepts(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string extension;
            try { extension = IOPath.GetExtension(path); }
            catch (ArgumentException) { return false; }
            if (BlockedExtensions.Contains(extension)) return false;
            return _extensions.Count == 0 || _extensions.Contains(extension);
        }

        public void Open()
        {
            string error;
            if (!EnsureExists(out error))
            {
                ModLoader.Logger.Warning("Could not open " + Path + ": " + error);
                return;
            }
            try
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string explorer = string.IsNullOrEmpty(windows) ? "explorer.exe" : IOPath.Combine(windows, "explorer.exe");
                using (Process.Start(new ProcessStartInfo(explorer, "\"" + Path + "\"") { UseShellExecute = false })) { }
            }
            catch (Exception e)
            {
                ModLoader.Logger.Warning("Could not open " + Path + ": " + e.Message);
            }
        }

        public override string ToString() { return DisplayName + " (" + Path + ")"; }

        internal bool EnsureExists(out string error)
        {
            error = null;
            if (HasLinkBetween(ModDirectory, Path))
            {
                error = "Linking folders are not allowed";
                return false;
            }
            if (Directory.Exists(Path)) return true;
            if (File.Exists(Path))
            {
                error = "That file already exists";
                return false;
            }
            try
            {
                Directory.CreateDirectory(Path);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        internal void SetFiles(IReadOnlyList<string> files)
        {
            _files = files ?? NoFiles;
            int count = _files.Count;
            CountLabel = count == 0 ? "empty" : count == 1 ? "1 file" : count + " files";
        }

        internal void ShowImportMessage(string message, bool isError, double seconds)
        {
            ImportMessage = message;
            ImportMessageIsError = isError;
            ImportMessageUntil = DateTime.UtcNow.AddSeconds(seconds);
        }

        internal static IReadOnlyList<ResourceFolder> CreateAll(ModContainer owner, ModLogger logger)
        {
            var info = owner?.Info;
            var declarations = info?.Manifest?.Resources;
            if (declarations == null || declarations.Count == 0) return NoFolders;

            string modDirectory = info.Directory;
            if (string.IsNullOrEmpty(modDirectory) || SamePath(modDirectory, ModLoader.ModsDirectory))
            {
                logger.Warning("\"resources\" are missing their own mod folder; ignoring them.");
                return NoFolders;
            }
            modDirectory = TrimSeparators(IOPath.GetFullPath(modDirectory));

            var result = new List<ResourceFolder>();
            foreach (var declaration in declarations)
            {
                if (declaration == null) continue;
                var warnings = new List<string>();
                string error;
                var folder = Create(owner, modDirectory, declaration, warnings, out error);
                foreach (var warning in warnings) logger.Warning("Resource folder \"" + declaration.Folder + "\": " + warning + ".");
                if (folder == null)
                {
                    logger.Warning("Resource folder \"" + declaration.Folder + "\" ignored: " + error + ".");
                    continue;
                }
                if (result.Exists(f => string.Equals(f.Path, folder.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    logger.Warning("Resource folder \"" + declaration.Folder + "\" is declared twice; ignoring the second one.");
                    continue;
                }
                folder.Prepare(logger);
                result.Add(folder);
            }
            return result.ToArray();
        }

        private static ResourceFolder Create(ModContainer owner, string modDirectory, ResourceFolderDeclaration declaration, List<string> warnings, out string error)
        {
            string normalized, fullPath;
            if (!TryResolve(modDirectory, declaration.Folder, out normalized, out fullPath, out error)) return null;
            string[] extensions;
            if (!TryNormalizeExtensions(declaration.Extensions, warnings, out extensions, out error)) return null;
            return new ResourceFolder(owner, modDirectory, normalized, fullPath, declaration, extensions);
        }

        private void Prepare(ModLogger logger)
        {
            string error;
            Dictionary<string, FileStamp> scan = null;
            if (!EnsureExists(out error))
            {
                logger.Warning("Could not create resource folder " + Path + ": " + error);
            }
            else
            {
                scan = Scan(out error);
                if (scan == null) logger.Warning("Could not read resource folder " + Path + ": " + error);
            }
            if (scan == null) scan = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
            Reported = scan;
            LastScan = scan;
            SetFiles(SortedPaths(scan));
        }

        internal Dictionary<string, FileStamp> Scan(out string error)
        {
            error = null;
            var result = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(Path);
            bool top = true;
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                FileInfo[] files;
                DirectoryInfo[] subdirectories = null;
                try
                {
                    var info = new DirectoryInfo(directory);
                    files = info.GetFiles();
                    if (Recursive) subdirectories = info.GetDirectories();
                }
                catch (DirectoryNotFoundException)
                {
                    top = false;
                    continue;
                }
                catch (Exception e)
                {
                    if (top)
                    {
                        error = e.Message;
                        return null;
                    }
                    continue;
                }
                top = false;

                foreach (var file in files)
                {
                    try
                    {
                        if (!IsVisible(file.Attributes) || !Accepts(file.Name)) continue;
                        result[file.FullName] = new FileStamp(file.Length, file.LastWriteTimeUtc.Ticks);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    if (result.Count >= MaxFiles) return result;
                }

                if (subdirectories == null) continue;
                foreach (var subdirectory in subdirectories)
                {
                    try
                    {
                        var attributes = subdirectory.Attributes;
                        if (IsVisible(attributes) && (attributes & FileAttributes.ReparsePoint) == 0) pending.Push(subdirectory.FullName);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return result;
        }

        internal static bool IsVisible(FileAttributes attributes)
        {
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
        }

        internal static string[] SortedPaths(Dictionary<string, FileStamp> scan)
        {
            var paths = new string[scan.Count];
            scan.Keys.CopyTo(paths, 0);
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        internal static bool TryResolve(string modDirectory, string folder, out string normalized, out string fullPath, out string error)
        {
            normalized = null;
            fullPath = null;
            error = null;
            if (string.IsNullOrEmpty(folder) || folder.Trim().Length == 0)
            {
                error = "\"folder\" is missing";
                return false;
            }
            string text = folder.Trim().Replace('/', '\\');
            if (text.IndexOfAny(ForbiddenPathChars) >= 0 || text.StartsWith("\\", StringComparison.Ordinal) || IOPath.IsPathRooted(text))
            {
                error = "\"folder\" must be a relative path inside mod root";
                return false;
            }

            var parts = new List<string>();
            foreach (string part in text.Split('\\'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                {
                    error = "\"folder\" must not leave mod root";
                    return false;
                }
                if (part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) || part.StartsWith(" ", StringComparison.Ordinal))
                {
                    error = "folder names cannot start or end with a space or dot";
                    return false;
                }
                if (ReservedNames.Contains(part.Split('.')[0]))
                {
                    error = part + " is a reserved name on Windows";
                    return false;
                }
                parts.Add(part);
            }

            string root;
            try { root = TrimSeparators(IOPath.GetFullPath(modDirectory)); }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            normalized = parts.Count == 0 ? "." : string.Join("\\", parts.ToArray());
            string full;
            try { full = parts.Count == 0 ? root : TrimSeparators(IOPath.GetFullPath(IOPath.Combine(root, normalized))); }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            if (!IsInside(root, full))
            {
                error = "\"folder\" must stay inside mod root";
                return false;
            }
            if (HasLinkBetween(root, full))
            {
                error = "\"folder\" is a link";
                return false;
            }
            fullPath = full;
            return true;
        }

        internal static bool TryNormalizeExtensions(IList<string> declared, List<string> warnings, out string[] extensions, out string error)
        {
            extensions = NoFiles;
            error = null;
            if (declared == null || declared.Count == 0) return true;

            var list = new List<string>();
            foreach (string raw in declared)
            {
                string text = (raw ?? "").Trim().ToLowerInvariant();
                if (text.StartsWith("*", StringComparison.Ordinal)) text = text.Substring(1);
                if (!text.StartsWith(".", StringComparison.Ordinal)) text = "." + text;
                if (!IsValidExtension(text))
                {
                    warnings.Add("\"" + raw + "\" is not a file extension");
                    continue;
                }
                if (BlockedExtensions.Contains(text))
                {
                    warnings.Add(text + " files cannot be resources");
                    continue;
                }
                if (!list.Contains(text)) list.Add(text);
            }
            if (list.Count == 0)
            {
                error = "none of its \"extensions\" can be used";
                return false;
            }
            extensions = list.ToArray();
            return true;
        }

        private static bool IsValidExtension(string extension)
        {
            if (extension.Length < 2 || extension.Length > 17 || extension[0] != '.') return false;
            for (int i = 1; i < extension.Length; i++)
            {
                char c = extension[i];
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
            }
            return true;
        }

        internal static string NormalizeForLookup(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return ".";
            var parts = new List<string>();
            foreach (string part in folder.Trim().Split(Separators))
                if (part.Length > 0 && part != ".") parts.Add(part);
            return parts.Count == 0 ? "." : string.Join("\\", parts.ToArray());
        }

        internal static bool IsInside(string root, string path)
        {
            root = TrimSeparators(root);
            path = TrimSeparators(path);
            string prefix = root.EndsWith("\\", StringComparison.Ordinal) ? root : root + "\\";
            return string.Equals(root, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasLinkBetween(string root, string path)
        {
            root = TrimSeparators(root);
            string current = TrimSeparators(path);
            while (current != null && current.Length > root.Length && IsInside(root, current))
            {
                try
                {
                    if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
                }
                catch (Exception)
                {
                    return true;
                }
                current = IOPath.GetDirectoryName(current);
            }
            return false;
        }

        internal static string TrimSeparators(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string trimmed = path.TrimEnd(Separators);
            return trimmed.Length == 0 || trimmed.EndsWith(":", StringComparison.Ordinal) ? path : trimmed;
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(TrimSeparators(IOPath.GetFullPath(a)), TrimSeparators(IOPath.GetFullPath(b)), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string DefaultName(string folder)
        {
            if (folder == ".") return "Files";
            int slash = folder.LastIndexOf('\\');
            return slash >= 0 ? folder.Substring(slash + 1) : folder;
        }

        private static string[] ToArray(IReadOnlyList<string> list)
        {
            var array = new string[list.Count];
            for (int i = 0; i < array.Length; i++) array[i] = list[i];
            return array;
        }

        private static char[] BuildForbiddenPathChars()
        {
            var chars = new List<char>(IOPath.GetInvalidPathChars());
            foreach (char c in new[] { ':', '*', '?', '"', '<', '>', '|' })
                if (!chars.Contains(c)) chars.Add(c);
            return chars.ToArray();
        }
    }
}
