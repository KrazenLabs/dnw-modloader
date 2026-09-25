using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MelonLoader.Logging;


namespace MelonLoader
{
    public class MelonLogger
    {
        internal enum Level { Message, Warning, Error }

        internal static Action<string, string, Level> Sink;

        public static readonly ColorARGB DefaultMelonColor = ColorARGB.Cyan;
        public static readonly ColorARGB DefaultTextColor = ColorARGB.LightGray;

        private static readonly Regex AnsiCodes = new Regex("\u001b\\[(.*?)m");

        [ThreadStatic] private static bool _raisingCallbacks;

        public static event Action<ConsoleColor, ConsoleColor, string, string> MsgCallbackHandler;
        public static event Action<ColorARGB, ColorARGB, string, string> MsgDrawingCallbackHandler;
        public static event Action<string, string> WarningCallbackHandler;
        public static event Action<string, string> ErrorCallbackHandler;

        internal static void Write(string section, Level level, string text)
        {
            Write(section, level, text, DefaultMelonColor, DefaultTextColor, false);
        }

        private static void Write(string section, Level level, string text, ColorARGB sectionColor, ColorARGB textColor, bool pastel)
        {
            text = text ?? "null";
            var sink = Sink;
            if (sink != null)
            {
                try { sink(section, pastel ? AnsiCodes.Replace(text, "") : text, level); }
                catch { }
            }
            RaiseCallbacks(section, level, text, sectionColor, textColor);
        }

        private static void RaiseCallbacks(string section, Level level, string text, ColorARGB sectionColor, ColorARGB textColor)
        {
            if (_raisingCallbacks) return;
            _raisingCallbacks = true;
            try
            {
                if (level == Level.Warning)
                {
                    Invoke(WarningCallbackHandler, handler => ((Action<string, string>)handler)(section, text));
                }
                else if (level == Level.Error)
                {
                    Invoke(ErrorCallbackHandler, handler => ((Action<string, string>)handler)(section, text));
                }
                else
                {
                    ConsoleColor sectionConsole = LogColors.ToConsoleColor(sectionColor), textConsole = LogColors.ToConsoleColor(textColor);
                    Invoke(MsgCallbackHandler, handler => ((Action<ConsoleColor, ConsoleColor, string, string>)handler)(sectionConsole, textConsole, section, text));
                    Invoke(MsgDrawingCallbackHandler, handler => ((Action<ColorARGB, ColorARGB, string, string>)handler)(sectionColor, textColor, section, text));
                }
            }
            finally
            {
                _raisingCallbacks = false;
            }
        }

        private static void Invoke(Delegate handlers, Action<Delegate> call)
        {
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { call(handler); }
                catch (Exception e)
                {
                    var sink = Sink;
                    if (sink == null) continue;
                    try { sink(null, "A MelonLogger callback of " + (handler.Method.DeclaringType != null ? handler.Method.DeclaringType.FullName : "?") + " threw: " + e, Level.Error); }
                    catch { }
                }
            }
        }

        private static string Format(string txt, object[] args)
        {
            if (txt == null) return "null";
            if (args == null || args.Length == 0) return txt;
            try { return string.Format(txt, args); }
            catch (FormatException) { return txt; }
        }

        private static string Str(object obj) { return obj == null ? "null" : obj.ToString(); }

        private static void Message(string section, ColorARGB sectionColor, ColorARGB textColor, string text) { Write(section, Level.Message, text, sectionColor, textColor, false); }
        private static void Pastel(string section, ColorARGB sectionColor, ColorARGB textColor, string text) { Write(section, Level.Message, text, sectionColor, textColor, true); }

        public static void Msg(object obj) { Message(null, DefaultMelonColor, DefaultTextColor, Str(obj)); }
        public static void Msg(string txt) { Message(null, DefaultMelonColor, DefaultTextColor, txt); }
        public static void Msg(string txt, params object[] args) { Message(null, DefaultMelonColor, DefaultTextColor, Format(txt, args)); }
        public static void Msg(ConsoleColor color, object obj) { Message(null, DefaultMelonColor, LogColors.FromConsoleColor(color), Str(obj)); }
        public static void Msg(ConsoleColor color, string txt) { Message(null, DefaultMelonColor, LogColors.FromConsoleColor(color), txt); }
        public static void Msg(ConsoleColor color, string txt, params object[] args) { Message(null, DefaultMelonColor, LogColors.FromConsoleColor(color), Format(txt, args)); }
        public static void Msg(ColorARGB color, object obj) { Message(null, DefaultMelonColor, color, Str(obj)); }
        public static void Msg(ColorARGB color, string txt) { Message(null, DefaultMelonColor, color, txt); }
        public static void Msg(ColorARGB color, string txt, params object[] args) { Message(null, DefaultMelonColor, color, Format(txt, args)); }

        public static void MsgDirect(ColorARGB color, string txt) { Message(null, DefaultMelonColor, color, txt); }

        // Pastel output is colour markup in MelonLoader's console; here it is plain text
        public static void MsgPastel(object obj) { Pastel(null, DefaultMelonColor, DefaultTextColor, Str(obj)); }
        public static void MsgPastel(string txt) { Pastel(null, DefaultMelonColor, DefaultTextColor, txt); }
        public static void MsgPastel(string txt, params object[] args) { Pastel(null, DefaultMelonColor, DefaultTextColor, Format(txt, args)); }
        public static void MsgPastel(ConsoleColor color, object obj) { Pastel(null, DefaultMelonColor, LogColors.FromConsoleColor(color), Str(obj)); }
        public static void MsgPastel(ConsoleColor color, string txt) { Pastel(null, DefaultMelonColor, LogColors.FromConsoleColor(color), txt); }
        public static void MsgPastel(ConsoleColor color, string txt, params object[] args) { Pastel(null, DefaultMelonColor, LogColors.FromConsoleColor(color), Format(txt, args)); }
        public static void MsgPastel(ColorARGB color, object obj) { Pastel(null, DefaultMelonColor, color, Str(obj)); }
        public static void MsgPastel(ColorARGB color, string txt) { Pastel(null, DefaultMelonColor, color, txt); }
        public static void MsgPastel(ColorARGB color, string txt, params object[] args) { Pastel(null, DefaultMelonColor, color, Format(txt, args)); }
        public static void MsgPastelDirect(ColorARGB color, string txt) { Pastel(null, DefaultMelonColor, color, txt); }

        public static void Log(object obj) { Msg(obj); }
        public static void Log(string txt) { Msg(txt); }
        public static void Log(string txt, params object[] args) { Msg(txt, args); }
        public static void Log(ConsoleColor color, object obj) { Msg(color, obj); }
        public static void Log(ConsoleColor color, string txt) { Msg(color, txt); }
        public static void Log(ConsoleColor color, string txt, params object[] args) { Msg(color, txt, args); }

        public static void Warning(object obj) { Write(null, Level.Warning, Str(obj)); }
        public static void Warning(string txt) { Write(null, Level.Warning, txt); }
        public static void Warning(string txt, params object[] args) { Write(null, Level.Warning, Format(txt, args)); }
        public static void LogWarning(string txt) { Warning(txt); }
        public static void LogWarning(string txt, params object[] args) { Warning(txt, args); }

        public static void Error(object obj) { Write(null, Level.Error, Str(obj)); }
        public static void Error(string txt) { Write(null, Level.Error, txt); }
        public static void Error(string txt, params object[] args) { Write(null, Level.Error, Format(txt, args)); }
        public static void Error(string txt, Exception ex) { Write(null, Level.Error, txt + Environment.NewLine + ex); }
        public static void LogError(string txt) { Error(txt); }
        public static void LogError(string txt, params object[] args) { Error(txt, args); }

        public static void BigError(string namesection, string txt) { Write(namesection, Level.Error, txt); }

        public static void WriteLine(int length = 30) { Write(null, Level.Message, new string('-', Math.Max(0, length))); }
        public static void WriteLine(ColorARGB color, int length = 30) { Message(null, DefaultMelonColor, color, new string('-', Math.Max(0, length))); }

        // Per-melon logger
        public class Instance
        {
            private readonly string _section;
            private readonly ColorARGB _color;

            public Instance(string name) : this(name, DefaultMelonColor) { }
            public Instance(string name, ConsoleColor color) : this(name, LogColors.FromConsoleColor(color)) { }
            public Instance(string name, ColorARGB color) { _section = name; _color = color; }

            public void Msg(object obj) { Message(_section, _color, DefaultTextColor, Str(obj)); }
            public void Msg(string txt) { Message(_section, _color, DefaultTextColor, txt); }
            public void Msg(string txt, params object[] args) { Message(_section, _color, DefaultTextColor, Format(txt, args)); }
            public void Msg(ConsoleColor color, object obj) { Message(_section, _color, LogColors.FromConsoleColor(color), Str(obj)); }
            public void Msg(ConsoleColor color, string txt) { Message(_section, _color, LogColors.FromConsoleColor(color), txt); }
            public void Msg(ConsoleColor color, string txt, params object[] args) { Message(_section, _color, LogColors.FromConsoleColor(color), Format(txt, args)); }
            public void Msg(ColorARGB color, object obj) { Message(_section, _color, color, Str(obj)); }
            public void Msg(ColorARGB color, string txt) { Message(_section, _color, color, txt); }
            public void Msg(ColorARGB color, string txt, params object[] args) { Message(_section, _color, color, Format(txt, args)); }

            public void MsgPastel(object obj) { Pastel(_section, _color, DefaultTextColor, Str(obj)); }
            public void MsgPastel(string txt) { Pastel(_section, _color, DefaultTextColor, txt); }
            public void MsgPastel(string txt, params object[] args) { Pastel(_section, _color, DefaultTextColor, Format(txt, args)); }
            public void MsgPastel(ConsoleColor color, object obj) { Pastel(_section, _color, LogColors.FromConsoleColor(color), Str(obj)); }
            public void MsgPastel(ConsoleColor color, string txt) { Pastel(_section, _color, LogColors.FromConsoleColor(color), txt); }
            public void MsgPastel(ConsoleColor color, string txt, params object[] args) { Pastel(_section, _color, LogColors.FromConsoleColor(color), Format(txt, args)); }
            public void MsgPastel(ColorARGB color, object obj) { Pastel(_section, _color, color, Str(obj)); }
            public void MsgPastel(ColorARGB color, string txt) { Pastel(_section, _color, color, txt); }
            public void MsgPastel(ColorARGB color, string txt, params object[] args) { Pastel(_section, _color, color, Format(txt, args)); }

            public void Warning(object obj) { Write(_section, Level.Warning, Str(obj)); }
            public void Warning(string txt) { Write(_section, Level.Warning, txt); }
            public void Warning(string txt, params object[] args) { Write(_section, Level.Warning, Format(txt, args)); }

            public void Error(object obj) { Write(_section, Level.Error, Str(obj)); }
            public void Error(string txt) { Write(_section, Level.Error, txt); }
            public void Error(string txt, params object[] args) { Write(_section, Level.Error, Format(txt, args)); }
            public void Error(string txt, Exception ex) { Write(_section, Level.Error, txt + Environment.NewLine + ex); }

            public void BigError(string txt) { Write(_section, Level.Error, txt); }

            public void WriteLine(int length = 30) { Write(_section, Level.Message, new string('-', Math.Max(0, length))); }
            public void WriteLine(ColorARGB color, int length = 30) { Message(_section, _color, color, new string('-', Math.Max(0, length))); }
            public void WriteSpacer() { Write(_section, Level.Message, ""); }
        }
    }

    internal static class LogColors
    {
        private static readonly KeyValuePair<ConsoleColor, ColorARGB>[] Pairs =
        {
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.Black, ColorARGB.Black),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.DarkBlue, ColorARGB.DarkBlue),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.DarkGreen, ColorARGB.DarkGreen),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.DarkCyan, ColorARGB.DarkCyan),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.DarkRed, ColorARGB.DarkRed),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.DarkMagenta, ColorARGB.DarkMagenta),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.DarkYellow, ColorARGB.Yellow),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.Gray, ColorARGB.LightGray),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.DarkGray, ColorARGB.DarkGray),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.Blue, ColorARGB.CornflowerBlue),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.Green, ColorARGB.LimeGreen),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.Cyan, ColorARGB.Cyan),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.Red, ColorARGB.IndianRed),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.Magenta, ColorARGB.Magenta),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.Yellow, ColorARGB.Yellow),
            new KeyValuePair<ConsoleColor, ColorARGB>(ConsoleColor.White, ColorARGB.White),
        };

        public static ColorARGB FromConsoleColor(ConsoleColor color)
        {
            foreach (var pair in Pairs)
                if (pair.Key == color) return pair.Value;
            return ColorARGB.White;
        }

        public static ConsoleColor ToConsoleColor(ColorARGB color)
        {
            for (int i = Pairs.Length - 1; i >= 0; i--)
                if (Pairs[i].Value == color) return Pairs[i].Key;
            return ConsoleColor.White;
        }
    }
}
