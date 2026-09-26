using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DnWModLoader
{
    internal static class ResourceWatcher
    {
        internal const string CallbackName = nameof(Mod.OnResourcesChanged);
        private const int PollMilliseconds = 1000;
        private const int MaxPollMilliseconds = 10000;
        private const int SlowScanFactor = 20;
        private const int FileNamesLogged = 5;
        private static readonly TimeSpan UnreadableTimeout = TimeSpan.FromSeconds(10);

        private static readonly object Sync = new object();
        private static readonly List<ResourceFolder> Folders = new List<ResourceFolder>();
        private static readonly Queue<Action> MainThreadWork = new Queue<Action>();
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static Thread _thread;
        private static volatile bool _stopping;

        public static void Register(IReadOnlyList<ResourceFolder> folders)
        {
            if (folders == null || folders.Count == 0) return;
            lock (Sync)
            {
                foreach (var folder in folders)
                    if (!Folders.Contains(folder)) Folders.Add(folder);
                if (_thread != null || _stopping) return;
                _thread = new Thread(Run) { IsBackground = true, Name = "DnW resource watcher" };
                _thread.Start();
            }
        }

        public static void Unregister(IReadOnlyList<ResourceFolder> folders)
        {
            if (folders == null) return;
            lock (Sync)
                foreach (var folder in folders) Folders.Remove(folder);
        }

        public static void Poke()
        {
            Wake.Set();
        }

        public static void Post(Action action)
        {
            if (action == null) return;
            lock (Sync) MainThreadWork.Enqueue(action);
        }

        public static void DispatchPending()
        {
            while (true)
            {
                Action action;
                lock (Sync)
                {
                    if (MainThreadWork.Count == 0) return;
                    action = MainThreadWork.Dequeue();
                }
                try { action(); }
                catch (Exception e) { ModLoader.Logger.Exception(e, "Resource folder update failed"); }
            }
        }

        public static void Stop()
        {
            _stopping = true;
            Wake.Set();
        }

        private static void Run()
        {
            while (!_stopping)
            {
                ResourceFolder[] folders;
                lock (Sync) folders = Folders.ToArray();
                var clock = Stopwatch.StartNew();
                foreach (var folder in folders)
                {
                    if (_stopping) return;
                    try { Poll(folder); }
                    catch (Exception e) { ReportScanError(folder, e.Message); }
                }
                long wait = Math.Min(MaxPollMilliseconds, Math.Max(PollMilliseconds, clock.ElapsedMilliseconds * SlowScanFactor));
                Wake.WaitOne((int)wait);
            }
        }

        internal static void Poll(ResourceFolder folder)
        {
            string error;
            var scan = folder.Scan(out error);
            if (scan == null)
            {
                ReportScanError(folder, error);
                return;
            }
            folder.LastScanError = null;

            bool stable = Same(scan, folder.LastScan);
            folder.LastScan = scan;
            if (!stable) return;
            if (Same(scan, folder.Reported))
            {
                folder.UnreadableSince = default(DateTime);
                return;
            }

            var changes = Diff(folder.Reported, scan);
            if (!AllReadable(changes))
            {
                if (folder.UnreadableSince == default(DateTime)) folder.UnreadableSince = DateTime.UtcNow;
                if (DateTime.UtcNow - folder.UnreadableSince < UnreadableTimeout) return;
            }
            folder.UnreadableSince = default(DateTime);
            folder.Reported = scan;
            var files = ResourceFolder.SortedPaths(scan);
            Post(() => Deliver(folder, files, changes));
        }

        private static void ReportScanError(ResourceFolder folder, string error)
        {
            if (error == folder.LastScanError) return;
            folder.LastScanError = error;
            Post(() => ModLoader.Logger.Warning("Could not read resource folder " + folder.Path + ": " + error));
        }

        private static void Deliver(ResourceFolder folder, string[] files, ResourceChanges changes)
        {
            folder.SetFiles(files);
            var container = folder.Owner;
            string modName = container?.Info != null ? container.Info.Name : folder.ModId;
            ModLoader.Logger.Info("Resource folder " + folder.DisplayName + " of " + modName + ": " + changes + Names(changes) + ".");
            ModLoader.DispatchTo(container, CallbackName, m => m.OnResourcesChanged(folder, changes));
        }

        private static string Names(ResourceChanges changes)
        {
            if (changes.Count > FileNamesLogged) return "";
            var names = new List<string>();
            foreach (var path in changes.Added) names.Add(Path.GetFileName(path));
            foreach (var path in changes.Removed) names.Add(Path.GetFileName(path));
            foreach (var path in changes.Changed) names.Add(Path.GetFileName(path));
            return " (" + string.Join(", ", names.ToArray()) + ")";
        }

        internal static bool Same(Dictionary<string, FileStamp> a, Dictionary<string, FileStamp> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (var pair in a)
            {
                FileStamp other;
                if (!b.TryGetValue(pair.Key, out other)) return false;
                if (other.Length != pair.Value.Length || other.WriteTicks != pair.Value.WriteTicks) return false;
            }
            return true;
        }

        internal static ResourceChanges Diff(Dictionary<string, FileStamp> before, Dictionary<string, FileStamp> after)
        {
            before = before ?? new Dictionary<string, FileStamp>();
            var added = new List<string>();
            var removed = new List<string>();
            var changed = new List<string>();
            foreach (var pair in after)
            {
                FileStamp old;
                if (!before.TryGetValue(pair.Key, out old)) added.Add(pair.Key);
                else if (old.Length != pair.Value.Length || old.WriteTicks != pair.Value.WriteTicks) changed.Add(pair.Key);
            }
            foreach (var path in before.Keys)
                if (!after.ContainsKey(path)) removed.Add(path);
            return new ResourceChanges(Sorted(added), Sorted(removed), Sorted(changed));
        }

        private static string[] Sorted(List<string> paths)
        {
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths.ToArray();
        }

        private static bool AllReadable(ResourceChanges changes)
        {
            foreach (var path in changes.Added)
                if (!Readable(path)) return false;
            foreach (var path in changes.Changed)
                if (!Readable(path)) return false;
            return true;
        }

        private static bool Readable(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
