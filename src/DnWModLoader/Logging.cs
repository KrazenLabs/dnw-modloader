using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DnWModLoader.Logging
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Fatal = 4,
        None = 5,
    }

    public struct LogEntry
    {
        public DateTime Time;
        public LogLevel Level;
        public string Source;
        public string Message;

        public override string ToString()
        {
            return "[" + Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] [" + LevelTag(Level) + "] [" + Source + "] " + Message;
        }

        internal static string LevelTag(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DBG";
                case LogLevel.Info: return "INF";
                case LogLevel.Warning: return "WRN";
                case LogLevel.Error: return "ERR";
                case LogLevel.Fatal: return "FTL";
                default: return "???";
            }
        }
    }

    internal sealed class LogLine
    {
        public LogLine(LogEntry entry, string text, long sequence)
        {
            Entry = entry;
            Text = text;
            Sequence = sequence;
        }

        public LogEntry Entry { get; }
        public string Text { get; }
        public long Sequence { get; }
    }

    public static class Log
    {
        internal const int RecentCapacity = 500;
        private const string UnityEchoMarker = "[DnW] ";

        private static readonly object Sync = new object();
        private static readonly Queue<LogLine> Recent = new Queue<LogLine>(RecentCapacity);
        private static long _sequence;
        private static StreamWriter _writer;

        public static LogLevel MinimumLevel = LogLevel.Debug;

        public static bool EchoToUnity;

        public static string FilePath { get; private set; }

        public static bool IsFallback { get; private set; }

        public static event Action<LogEntry> EntryLogged;

        internal static void Open(string path, string fallbackPath = null)
        {
            lock (Sync)
            {
                var error = TryOpen(path);
                if (error == null) return;

                if (!string.IsNullOrEmpty(fallbackPath) && TryOpen(fallbackPath) == null)
                {
                    IsFallback = true;
                    try { UnityEngine.Debug.LogWarning(UnityEchoMarker + "Could not open log file " + path + " (" + error.Message + "); logging to " + fallbackPath + " instead."); } catch { }
                    return;
                }

                try { UnityEngine.Debug.LogWarning(UnityEchoMarker + "Could not open log file " + path + ": " + error.Message); } catch { }
            }
        }

        private static Exception TryOpen(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(path))
                {
                    string prev = Path.Combine(dir ?? "", Path.GetFileNameWithoutExtension(path) + ".prev" + Path.GetExtension(path));
                    try { File.Copy(path, prev, true); } catch { /* best effort */ }
                }
                _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
                _writer.AutoFlush = true;
                FilePath = path;
                return null;
            }
            catch (Exception e)
            {
                _writer = null;
                FilePath = null;
                return e;
            }
        }

        internal static void Close()
        {
            lock (Sync)
            {
                try { _writer?.Flush(); _writer?.Dispose(); } catch { }
                _writer = null;
            }
        }

        public static void Write(LogLevel level, string source, string message)
        {
            if (level < MinimumLevel || level == LogLevel.None) return;
            var entry = new LogEntry { Time = DateTime.Now, Level = level, Source = source ?? "?", Message = message ?? "" };
            string line = entry.ToString();

            lock (Sync)
            {
                if (Recent.Count >= RecentCapacity) Recent.Dequeue();
                Recent.Enqueue(new LogLine(entry, line, ++_sequence));
                try { _writer?.WriteLine(line); } catch { }
            }

            if (EchoToUnity)
            {
                try
                {
                    string unityLine = UnityEchoMarker + line;
                    if (level >= LogLevel.Error) UnityEngine.Debug.LogError(unityLine);
                    else if (level == LogLevel.Warning) UnityEngine.Debug.LogWarning(unityLine);
                    else UnityEngine.Debug.Log(unityLine);
                }
                catch { }
            }

            var handler = EntryLogged;
            if (handler != null)
            {
                try { handler(entry); } catch { }
            }
        }

        public static LogEntry[] GetRecent()
        {
            lock (Sync)
            {
                var entries = new LogEntry[Recent.Count];
                int i = 0;
                foreach (var line in Recent) entries[i++] = line.Entry;
                return entries;
            }
        }

        internal static long CopyRecent(long after, List<LogLine> target)
        {
            lock (Sync)
            {
                if (_sequence > after)
                    foreach (var line in Recent)
                        if (line.Sequence > after) target.Add(line);
                return _sequence;
            }
        }

        internal static bool IsEchoedLine(string condition)
        {
            return condition != null && condition.StartsWith(UnityEchoMarker, StringComparison.Ordinal);
        }
    }

    public sealed class ModLogger
    {
        public string Source { get; }

        public ModLogger(string source)
        {
            Source = string.IsNullOrEmpty(source) ? "?" : source;
        }

        public void Debug(string message) { Log.Write(LogLevel.Debug, Source, message); }
        public void Info(string message) { Log.Write(LogLevel.Info, Source, message); }
        public void Warning(string message) { Log.Write(LogLevel.Warning, Source, message); }
        public void Error(string message) { Log.Write(LogLevel.Error, Source, message); }
        public void Fatal(string message) { Log.Write(LogLevel.Fatal, Source, message); }

        public void Exception(Exception exception, string context = null)
        {
            string text = string.IsNullOrEmpty(context) ? "" : context + ": ";
            Log.Write(LogLevel.Error, Source, text + Describe(exception));
        }

        public void Write(LogLevel level, string message) { Log.Write(level, Source, message); }

        // Just one line
        internal static string Brief(Exception exception)
        {
            if (exception == null) return "(null exception)";
            var invocation = exception as System.Reflection.TargetInvocationException;
            if (invocation != null && invocation.InnerException != null) exception = invocation.InnerException;
            return exception.GetType().Name + ": " + exception.Message;
        }

        internal static string Describe(Exception exception)
        {
            if (exception == null) return "(null exception)";
            var sb = new StringBuilder();
            var current = exception;
            int depth = 0;
            while (current != null && depth < 8)
            {
                if (depth > 0) sb.Append("\n  --> inner: ");
                sb.Append(current.GetType().FullName).Append(": ").Append(current.Message);
                if (current is System.Reflection.ReflectionTypeLoadException rtle && rtle.LoaderExceptions != null)
                {
                    foreach (var le in rtle.LoaderExceptions)
                        if (le != null) sb.Append("\n    loader: ").Append(le.Message);
                }
                if (!string.IsNullOrEmpty(current.StackTrace))
                    sb.Append("\n").Append(current.StackTrace);
                current = current.InnerException;
                depth++;
            }
            return sb.ToString();
        }
    }
}
