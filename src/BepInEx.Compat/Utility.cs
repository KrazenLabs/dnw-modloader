using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;

namespace BepInEx
{
    public static class Utility
    {
        private static bool? _dynamicAssemblies;

        public static bool CLRSupportsDynamicAssemblies
        {
            get
            {
                if (_dynamicAssemblies.HasValue) return _dynamicAssemblies.Value;
                try
                {
                    new CustomAttributeBuilder(null, new object[0]);
                    _dynamicAssemblies = true;
                }
                catch (PlatformNotSupportedException) { _dynamicAssemblies = false; }
                catch (ArgumentNullException) { _dynamicAssemblies = true; }
                return _dynamicAssemblies.Value;
            }
        }

        public static Encoding UTF8NoBom { get; } = new UTF8Encoding(false);

        public static bool TryDo(Action action, out Exception exception)
        {
            exception = null;
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                exception = e;
                return false;
            }
        }

        public static string CombinePaths(params string[] parts)
        {
            return parts.Aggregate(Path.Combine);
        }

        public static string ParentDirectory(string path, int levels = 1)
        {
            for (int i = 0; i < levels; i++) path = Path.GetDirectoryName(path);
            return path;
        }

        public static bool SafeParseBool(string input, bool defaultValue = false)
        {
            return bool.TryParse(input, out bool result) ? result : defaultValue;
        }

        public static string ConvertToWWWFormat(string path)
        {
            return "file://" + path.Replace('\\', '/');
        }

        public static bool IsNullOrWhiteSpace(this string self)
        {
            return self == null || self.All(char.IsWhiteSpace);
        }

        public static IEnumerable<TNode> TopologicalSort<TNode>(IEnumerable<TNode> nodes, Func<TNode, IEnumerable<TNode>> dependencySelector)
        {
            var result = new List<TNode>();
            var done = new HashSet<TNode>();
            var inProgress = new HashSet<TNode>();
            var path = new Stack<TNode>();

            void Visit(TNode node)
            {
                if (done.Contains(node)) return;
                if (!inProgress.Add(node))
                    throw new Exception("Cyclic Dependency:\r\n" + string.Join("\r\n", path.Select(x => " - " + x).ToArray()));
                path.Push(node);
                foreach (var dependency in dependencySelector(node)) Visit(dependency);
                path.Pop();
                inProgress.Remove(node);
                done.Add(node);
                result.Add(node);
            }

            foreach (var node in nodes) Visit(node);
            return result;
        }

        public static bool TryResolveDllAssembly(AssemblyName assemblyName, string directory, out Assembly assembly)
        {
            assembly = null;
            if (!Directory.Exists(directory)) return false;
            var directories = new List<string> { directory };
            directories.AddRange(Directory.GetDirectories(directory, "*", SearchOption.AllDirectories));
            foreach (var dir in directories)
            {
                string candidate = Path.Combine(dir, assemblyName.Name + ".dll");
                if (!File.Exists(candidate)) continue;
                try
                {
                    assembly = Assembly.LoadFile(candidate);
                    return true;
                }
                catch { }
            }
            return false;
        }

        public static bool TryOpenFileStream(string path, FileMode mode, out FileStream fileStream, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.Read)
        {
            try
            {
                fileStream = new FileStream(path, mode, access, share);
                return true;
            }
            catch (IOException)
            {
                fileStream = null;
                return false;
            }
        }

        public static bool TryParseAssemblyName(string fullName, out AssemblyName assemblyName)
        {
            try
            {
                assemblyName = new AssemblyName(fullName);
                return true;
            }
            catch
            {
                assemblyName = null;
                return false;
            }
        }

        public static IEnumerable<string> GetUniqueFilesInDirectories(IEnumerable<string> directories, string pattern = "*")
        {
            var files = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var directory in directories)
                foreach (var file in Directory.GetFiles(directory, pattern))
                {
                    string name = Path.GetFileName(file);
                    if (!files.ContainsKey(name)) files[name] = file;
                }
            return files.Values;
        }
    }

    public static class ThreadingExtensions
    {
        public static IEnumerable<TOut> RunParallel<TIn, TOut>(this IEnumerable<TIn> data, Func<TIn, TOut> work, int workerCount = -1)
        {
            return RunParallel((IList<TIn>)data.ToList(), work, workerCount);
        }

        // Results come back in no particular order
        public static IEnumerable<TOut> RunParallel<TIn, TOut>(this IList<TIn> data, Func<TIn, TOut> work, int workerCount = -1)
        {
            if (workerCount < 0) workerCount = Math.Max(2, Environment.ProcessorCount);
            else if (workerCount == 0) throw new ArgumentException("Need at least 1 worker", nameof(workerCount));

            var results = new List<TOut>(data.Count);
            if (data.Count == 0) return results;
            int perWorker = (data.Count + workerCount - 1) / workerCount;
            int pending = 0;
            Exception failure = null;
            var sync = new object();
            using (var finished = new ManualResetEvent(false))
            {
                for (int w = 0; w < workerCount; w++)
                {
                    int first = w * perWorker, last = Math.Min(first + perWorker, data.Count);
                    if (first >= last) break;
                    Interlocked.Increment(ref pending);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        var local = new List<TOut>(last - first);
                        try
                        {
                            for (int i = first; i < last && failure == null; i++) local.Add(work(data[i]));
                        }
                        catch (Exception e) { failure = e; }
                        lock (sync) results.AddRange(local);
                        if (Interlocked.Decrement(ref pending) == 0) finished.Set();
                    });
                }
                finished.WaitOne();
            }
            if (failure != null) throw new TargetInvocationException("An exception was thrown inside one of the threads", failure);
            return results;
        }
    }

    public sealed class ThreadingHelper : MonoBehaviour, ISynchronizeInvoke
    {
        private readonly object _queueLock = new object();
        private Action _queue;
        private Thread _mainThread;

        public static ThreadingHelper Instance { get; private set; }

        public static ISynchronizeInvoke SynchronizingObject { get { return Instance; } }

        public bool InvokeRequired { get { return _mainThread == null || _mainThread != Thread.CurrentThread; } }

        internal static void Initialize()
        {
            if (Instance != null) return;
            var host = new GameObject("BepInEx_ThreadingHelper");
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<ThreadingHelper>();
            Instance._mainThread = Thread.CurrentThread;
        }

        public void StartSyncInvoke(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (_queueLock) _queue += action;
        }

        public void StartAsyncInvoke(Func<Action> action)
        {
            if (!ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var next = action();
                    if (next != null) StartSyncInvoke(next);
                }
                catch (Exception e) { LogFailure(e); }
            }))
                throw new NotSupportedException("Failed to queue the action on ThreadPool");
        }

        private void Update()
        {
            if (_mainThread == null) _mainThread = Thread.CurrentThread;
            if (_queue == null) return;
            Action queued;
            lock (_queueLock)
            {
                queued = _queue;
                _queue = null;
            }
            foreach (Action action in queued.GetInvocationList())
            {
                try { action(); }
                catch (Exception e) { LogFailure(e); }
            }
        }

        private static void LogFailure(Exception e)
        {
            Logging.Logger.Log(LogLevel.Error, e);
        }

        IAsyncResult ISynchronizeInvoke.BeginInvoke(Delegate method, object[] args)
        {
            var result = new InvokeResult();
            if (!InvokeRequired) result.Run(method, args, true);
            else StartSyncInvoke(() => result.Run(method, args, false));
            return result;
        }

        object ISynchronizeInvoke.EndInvoke(IAsyncResult asyncResult)
        {
            var result = (InvokeResult)asyncResult;
            result.AsyncWaitHandle.WaitOne();
            if (result.Failure != null) throw result.Failure;
            return result.AsyncState;
        }

        object ISynchronizeInvoke.Invoke(Delegate method, object[] args)
        {
            var sync = (ISynchronizeInvoke)this;
            return sync.EndInvoke(sync.BeginInvoke(method, args));
        }

        private sealed class InvokeResult : IAsyncResult
        {
            private readonly ManualResetEvent _done = new ManualResetEvent(false);

            public Exception Failure;
            public bool IsCompleted { get; private set; }
            public WaitHandle AsyncWaitHandle { get { return _done; } }
            public object AsyncState { get; private set; }
            public bool CompletedSynchronously { get; private set; }

            public void Run(Delegate method, object[] args, bool synchronously)
            {
                try { AsyncState = method.DynamicInvoke(args); }
                catch (Exception e) { Failure = e; }
                CompletedSynchronously = synchronously;
                IsCompleted = true;
                _done.Set();
            }
        }
    }
}
