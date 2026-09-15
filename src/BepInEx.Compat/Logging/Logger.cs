using System;
using System.Collections;
using System.Collections.Generic;

namespace BepInEx.Logging
{
    [Flags]
    public enum LogLevel
    {
        None = 0,
        Fatal = 1,
        Error = 2,
        Warning = 4,
        Message = 8,
        Info = 16,
        Debug = 32,
        All = Fatal | Error | Warning | Message | Info | Debug,
    }

    public interface ILogSource : IDisposable
    {
        string SourceName { get; }

        event EventHandler<LogEventArgs> LogEvent;
    }

    public interface ILogListener : IDisposable
    {
        void LogEvent(object sender, LogEventArgs eventArgs);
    }

    public class LogEventArgs : EventArgs
    {
        public object Data { get; protected set; }
        public LogLevel Level { get; protected set; }
        public ILogSource Source { get; protected set; }

        public LogEventArgs(object data, LogLevel level, ILogSource source)
        {
            Data = data;
            Level = level;
            Source = source;
        }

        public override string ToString()
        {
            return string.Format("[{0,-7}:{1,10}] {2}", Level, Source != null ? Source.SourceName : "", Data);
        }

        public string ToStringLine()
        {
            return ToString() + Environment.NewLine;
        }
    }

    public class ManualLogSource : ILogSource
    {
        public string SourceName { get; }

        public event EventHandler<LogEventArgs> LogEvent;

        public ManualLogSource(string sourceName)
        {
            SourceName = sourceName;
        }

        public void Log(LogLevel level, object data)
        {
            LogEvent?.Invoke(this, new LogEventArgs(data, level, this));
        }

        public void LogFatal(object data) { Log(LogLevel.Fatal, data); }
        public void LogError(object data) { Log(LogLevel.Error, data); }
        public void LogWarning(object data) { Log(LogLevel.Warning, data); }
        public void LogMessage(object data) { Log(LogLevel.Message, data); }
        public void LogInfo(object data) { Log(LogLevel.Info, data); }
        public void LogDebug(object data) { Log(LogLevel.Debug, data); }

        public void Dispose() { }
    }

    public static class Logger
    {
        private static readonly ListenerList ListenerItems = new ListenerList();
        private static readonly SourceList SourceItems = new SourceList();
        private static readonly ManualLogSource InternalSource = CreateLogSource("BepInEx");

        public static ICollection<ILogListener> Listeners { get { return ListenerItems; } }

        public static ICollection<ILogSource> Sources { get { return SourceItems; } }

        public static ManualLogSource CreateLogSource(string sourceName)
        {
            var source = new ManualLogSource(sourceName);
            Sources.Add(source);
            return source;
        }

        internal static void Log(LogLevel level, object data) { InternalSource.Log(level, data); }
        internal static void LogError(object data) { Log(LogLevel.Error, data); }
        internal static void LogWarning(object data) { Log(LogLevel.Warning, data); }
        internal static void LogInfo(object data) { Log(LogLevel.Info, data); }
        internal static void LogDebug(object data) { Log(LogLevel.Debug, data); }

        private static void Dispatch(object sender, LogEventArgs eventArgs)
        {
            foreach (var listener in ListenerItems.Snapshot)
            {
                // Skip broken listeners
                try { listener.LogEvent(sender, eventArgs); }
                catch { }
            }
        }

        private class SnapshotCollection<T> : ICollection<T> where T : class
        {
            protected readonly object Sync = new object();
            private T[] _items = new T[0];

            public T[] Snapshot { get { return _items; } }
            public int Count { get { return _items.Length; } }
            public bool IsReadOnly { get { return false; } }

            public virtual void Add(T item)
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                lock (Sync)
                {
                    var next = new T[_items.Length + 1];
                    Array.Copy(_items, next, _items.Length);
                    next[_items.Length] = item;
                    _items = next;
                }
            }

            public virtual bool Remove(T item)
            {
                if (item == null) return false;
                lock (Sync)
                {
                    var next = new List<T>(_items);
                    bool removed = next.Remove(item);
                    _items = next.ToArray();
                    return removed;
                }
            }

            public virtual void Clear()
            {
                lock (Sync) _items = new T[0];
            }

            public bool Contains(T item) { return Array.IndexOf(_items, item) >= 0; }
            public void CopyTo(T[] array, int arrayIndex) { _items.CopyTo(array, arrayIndex); }
            public IEnumerator<T> GetEnumerator() { return ((IEnumerable<T>)_items).GetEnumerator(); }
            IEnumerator IEnumerable.GetEnumerator() { return _items.GetEnumerator(); }
        }

        private sealed class ListenerList : SnapshotCollection<ILogListener> { }

        private sealed class SourceList : SnapshotCollection<ILogSource>
        {
            public override void Add(ILogSource item)
            {
                lock (Sync)
                {
                    base.Add(item);
                    item.LogEvent += Dispatch;
                }
            }

            public override bool Remove(ILogSource item)
            {
                lock (Sync)
                {
                    bool removed = base.Remove(item);
                    if (removed) item.LogEvent -= Dispatch;
                    return removed;
                }
            }

            public override void Clear()
            {
                lock (Sync)
                {
                    foreach (var source in Snapshot) source.LogEvent -= Dispatch;
                    base.Clear();
                }
            }
        }
    }

    public static class LogLevelExtensions
    {
        private static readonly LogLevel[] Ordered = { LogLevel.Fatal, LogLevel.Error, LogLevel.Warning, LogLevel.Message, LogLevel.Info, LogLevel.Debug };

        public static LogLevel GetHighestLevel(this LogLevel levels)
        {
            foreach (var level in Ordered)
                if ((levels & level) != 0) return level;
            return LogLevel.None;
        }

        public static ConsoleColor GetConsoleColor(this LogLevel level)
        {
            switch (level.GetHighestLevel())
            {
                case LogLevel.Fatal: return ConsoleColor.Red;
                case LogLevel.Error: return ConsoleColor.DarkRed;
                case LogLevel.Warning: return ConsoleColor.Yellow;
                case LogLevel.Message: return ConsoleColor.White;
                case LogLevel.Debug: return ConsoleColor.DarkGray;
                default: return ConsoleColor.Gray;
            }
        }
    }
}
