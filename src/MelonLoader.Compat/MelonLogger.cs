using System;
using MelonLoader.Logging;


namespace MelonLoader
{
    public class MelonLogger
    {
        internal enum Level { Message, Warning, Error }

        internal static Action<string, string, Level> Sink;

        internal static void Write(string section, Level level, string text)
        {
            var sink = Sink;
            if (sink == null) return;
            try { sink(section, text ?? "null", level); }
            catch { }
        }

        private static string Format(string txt, object[] args)
        {
            if (txt == null) return "null";
            if (args == null || args.Length == 0) return txt;
            try { return string.Format(txt, args); }
            catch (FormatException) { return txt; }
        }

        private static string Str(object obj) { return obj == null ? "null" : obj.ToString(); }

        public static void Msg(object obj) { Write(null, Level.Message, Str(obj)); }
        public static void Msg(string txt) { Write(null, Level.Message, txt); }
        public static void Msg(string txt, params object[] args) { Write(null, Level.Message, Format(txt, args)); }
        public static void Msg(ConsoleColor color, object obj) { Write(null, Level.Message, Str(obj)); }
        public static void Msg(ConsoleColor color, string txt) { Write(null, Level.Message, txt); }
        public static void Msg(ConsoleColor color, string txt, params object[] args) { Write(null, Level.Message, Format(txt, args)); }
        public static void Msg(ColorARGB color, object obj) { Write(null, Level.Message, Str(obj)); }
        public static void Msg(ColorARGB color, string txt) { Write(null, Level.Message, txt); }
        public static void Msg(ColorARGB color, string txt, params object[] args) { Write(null, Level.Message, Format(txt, args)); }

        public static void MsgDirect(ColorARGB color, string txt) { Write(null, Level.Message, txt); }

        // Pastel output is colour markup in MelonLoader's console; here it is plain text
        public static void MsgPastel(object obj) { Write(null, Level.Message, Str(obj)); }
        public static void MsgPastel(string txt) { Write(null, Level.Message, txt); }
        public static void MsgPastel(string txt, params object[] args) { Write(null, Level.Message, Format(txt, args)); }
        public static void MsgPastel(ConsoleColor color, object obj) { Write(null, Level.Message, Str(obj)); }
        public static void MsgPastel(ConsoleColor color, string txt) { Write(null, Level.Message, txt); }
        public static void MsgPastel(ConsoleColor color, string txt, params object[] args) { Write(null, Level.Message, Format(txt, args)); }
        public static void MsgPastel(ColorARGB color, object obj) { Write(null, Level.Message, Str(obj)); }
        public static void MsgPastel(ColorARGB color, string txt) { Write(null, Level.Message, txt); }
        public static void MsgPastel(ColorARGB color, string txt, params object[] args) { Write(null, Level.Message, Format(txt, args)); }
        public static void MsgPastelDirect(ColorARGB color, string txt) { Write(null, Level.Message, txt); }

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
        public static void WriteLine(ColorARGB color, int length = 30) { WriteLine(length); }

        public static event Action<string, string, ConsoleColor, ConsoleColor> MsgCallbackHandler;
        public static event Action<string, string, ConsoleColor, ConsoleColor> MsgDrawingCallbackHandler;
        public static event Action<string, string> WarningCallbackHandler;
        public static event Action<string, string> ErrorCallbackHandler;

        // Per-melon logger
        public class Instance
        {
            private readonly string _section;

            public Instance(string name) { _section = name; }
            public Instance(string name, ConsoleColor color) { _section = name; }
            public Instance(string name, ColorARGB color) { _section = name; }

            public void Msg(object obj) { Write(_section, Level.Message, Str(obj)); }
            public void Msg(string txt) { Write(_section, Level.Message, txt); }
            public void Msg(string txt, params object[] args) { Write(_section, Level.Message, Format(txt, args)); }
            public void Msg(ConsoleColor color, object obj) { Msg(obj); }
            public void Msg(ConsoleColor color, string txt) { Msg(txt); }
            public void Msg(ConsoleColor color, string txt, params object[] args) { Msg(txt, args); }
            public void Msg(ColorARGB color, object obj) { Msg(obj); }
            public void Msg(ColorARGB color, string txt) { Msg(txt); }
            public void Msg(ColorARGB color, string txt, params object[] args) { Msg(txt, args); }

            public void MsgPastel(object obj) { Msg(obj); }
            public void MsgPastel(string txt) { Msg(txt); }
            public void MsgPastel(string txt, params object[] args) { Msg(txt, args); }
            public void MsgPastel(ConsoleColor color, object obj) { Msg(obj); }
            public void MsgPastel(ConsoleColor color, string txt) { Msg(txt); }
            public void MsgPastel(ConsoleColor color, string txt, params object[] args) { Msg(txt, args); }
            public void MsgPastel(ColorARGB color, object obj) { Msg(obj); }
            public void MsgPastel(ColorARGB color, string txt) { Msg(txt); }
            public void MsgPastel(ColorARGB color, string txt, params object[] args) { Msg(txt, args); }

            public void Warning(object obj) { Write(_section, Level.Warning, Str(obj)); }
            public void Warning(string txt) { Write(_section, Level.Warning, txt); }
            public void Warning(string txt, params object[] args) { Write(_section, Level.Warning, Format(txt, args)); }

            public void Error(object obj) { Write(_section, Level.Error, Str(obj)); }
            public void Error(string txt) { Write(_section, Level.Error, txt); }
            public void Error(string txt, params object[] args) { Write(_section, Level.Error, Format(txt, args)); }
            public void Error(string txt, Exception ex) { Write(_section, Level.Error, txt + Environment.NewLine + ex); }

            public void BigError(string txt) { Write(_section, Level.Error, txt); }

            public void WriteLine(int length = 30) { Write(_section, Level.Message, new string('-', Math.Max(0, length))); }
            public void WriteLine(ColorARGB color, int length = 30) { WriteLine(length); }
            public void WriteSpacer() { Write(_section, Level.Message, ""); }
        }
    }
}
