using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace BepInEx.Logging
{
    // Imitates BepInEx's LogOutput.log
    public class DiskLogListener : ILogListener
    {
        public LogLevel DisplayedLogLevel { get; set; }
        public TextWriter LogWriter { get; protected set; }
        public System.Threading.Timer FlushTimer { get; protected set; }
        public bool WriteFromUnityLog { get; set; }

        public DiskLogListener(string localPath, LogLevel displayedLogLevel = LogLevel.Info, bool appendLog = false, bool includeUnityLog = false)
        {
            DisplayedLogLevel = displayedLogLevel;
            WriteFromUnityLog = includeUnityLog;

            string root = Paths.BepInExRootPath ?? ".";
            FileStream stream = null;
            for (int attempt = 0; attempt < 5 && stream == null; attempt++)
            {
                string name = attempt == 0 ? localPath : localPath + "." + attempt;
                try
                {
                    Directory.CreateDirectory(root);
                    stream = new FileStream(Path.Combine(root, name), appendLog ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { break; }
            }
            if (stream == null)
            {
                Logger.LogError("Couldn't open a log file for writing. Skipping log file creation");
                return;
            }
            LogWriter = TextWriter.Synchronized(new StreamWriter(stream, Utility.UTF8NoBom));
            FlushTimer = new System.Threading.Timer(_ => { try { LogWriter?.Flush(); } catch { } }, null, 2000, 2000);
        }

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (LogWriter == null) return;
            if (!WriteFromUnityLog && eventArgs.Source is UnityLogSource) return;
            if ((eventArgs.Level & DisplayedLogLevel) == 0) return;
            LogWriter.WriteLine(eventArgs.ToString());
        }

        public void Dispose()
        {
            FlushTimer?.Dispose();
            try { LogWriter?.Flush(); LogWriter?.Dispose(); } catch { }
            LogWriter = null;
        }

        ~DiskLogListener()
        {
            Dispose();
        }
    }

    // There is no console window in the DnW Mod Loader
    public class ConsoleLogListener : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs eventArgs) { }

        public void Dispose() { }
    }

    public class UnityLogListener : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (eventArgs.Source is UnityLogSource) return;
            UnityEngine.Debug.Log(eventArgs.ToString());
        }

        public void Dispose() { }
    }

    // Unity's own log messages
    public class UnityLogSource : ILogSource
    {
        private static event EventHandler<LogEventArgs> UnityMessage;
        private bool _disposed;

        public string SourceName { get; } = "Unity Log";

        public event EventHandler<LogEventArgs> LogEvent;

        static UnityLogSource()
        {
            Application.logMessageReceived += OnUnityLogMessage;
        }

        public UnityLogSource()
        {
            UnityMessage += Forward;
        }

        private void Forward(object sender, LogEventArgs eventArgs)
        {
            LogEvent?.Invoke(this, new LogEventArgs(eventArgs.Data, eventArgs.Level, this));
        }

        private static void OnUnityLogMessage(string message, string stackTrace, LogType type)
        {
            LogLevel level;
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    level = LogLevel.Error;
                    break;
                case LogType.Warning:
                    level = LogLevel.Warning;
                    break;
                default:
                    level = LogLevel.Info;
                    break;
            }
            if (type == LogType.Exception) message += "\nStack trace:\n" + stackTrace;
            UnityMessage?.Invoke(null, new LogEventArgs(message, level, null));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnityMessage -= Forward;
        }
    }

    // System.Diagnostics.Trace output
    public class TraceLogSource : TraceListener
    {
        private static TraceLogSource _instance;

        public static bool IsListening { get; protected set; }

        protected ManualLogSource LogSource { get; }

        public static ILogSource CreateSource()
        {
            if (_instance == null)
            {
                _instance = new TraceLogSource();
                Trace.Listeners.Add(_instance);
                IsListening = true;
            }
            return _instance.LogSource;
        }

        protected TraceLogSource()
        {
            LogSource = new ManualLogSource("Trace");
        }

        public override void Write(string message) { LogSource.LogInfo(message); }

        public override void WriteLine(string message) { LogSource.LogInfo(message); }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            TraceEvent(eventCache, source, eventType, id, string.Format(format, args));
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            LogLevel level;
            switch (eventType)
            {
                case TraceEventType.Critical: level = LogLevel.Fatal; break;
                case TraceEventType.Error: level = LogLevel.Error; break;
                case TraceEventType.Warning: level = LogLevel.Warning; break;
                case TraceEventType.Information: level = LogLevel.Info; break;
                default: level = LogLevel.Debug; break;
            }
            LogSource.Log(level, (message ?? "").Trim());
        }
    }
}
