using DnWModLoader.Logging;
using HarmonyLogger = HarmonyLib.Tools.Logger;

namespace DnWModLoader
{
    internal static class HarmonyLog
    {
        private const string Source = "HarmonyX";
        private static bool _forwarding;

        public static void Forward()
        {
            if (_forwarding) return;
            _forwarding = true;
            HarmonyLogger.ChannelFilter |= HarmonyLogger.LogChannel.Warn | HarmonyLogger.LogChannel.Error;
            HarmonyLogger.MessageReceived += OnMessage;
        }

        private static void OnMessage(object sender, HarmonyLogger.LogEventArgs args)
        {
            var channel = args.LogChannel;
            var level = (channel & HarmonyLogger.LogChannel.Error) != 0 ? LogLevel.Error
                : (channel & HarmonyLogger.LogChannel.Warn) != 0 ? LogLevel.Warning
                : LogLevel.Debug;
            Log.Write(level, Source, args.Message);
        }
    }
}
