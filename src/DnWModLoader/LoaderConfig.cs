using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DnWModLoader.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DnWModLoader
{
    public enum UnityLogMirror
    {
        None,
        Error,
        Warning,
        All,
    }

    // Settings of the loader itself
    public sealed class LoaderConfig
    {
        public const string FileName = "ModLoader.json";

        [JsonProperty("overlayHotkey")]
        public string OverlayHotkey { get; set; } = "F10";

        [JsonProperty("showOverlayOnStart")]
        public bool ShowOverlayOnStart { get; set; } = false;

        [JsonProperty("showStartupBanner")]
        public bool ShowStartupBanner { get; set; } = true;

        [JsonProperty("startupBannerSeconds")]
        public float StartupBannerSeconds { get; set; } = 12f;

        // Overlay delay; Opening it too early can crash the game
        [JsonProperty("overlayOpenDelayAfterSceneLoad")]
        public float OverlayOpenDelayAfterSceneLoad { get; set; } = 5f;

        [JsonProperty("logLevel")]
        [JsonConverter(typeof(StringEnumConverter))]
        public LogLevel LogLevel { get; set; } = LogLevel.Debug;

        [JsonProperty("mirrorUnityLog")]
        [JsonConverter(typeof(StringEnumConverter))]
        public UnityLogMirror MirrorUnityLog { get; set; } = UnityLogMirror.Error;

        [JsonProperty("echoLoaderLogToUnity")]
        public bool EchoLoaderLogToUnity { get; set; } = false;

        [JsonProperty("disabledMods")]
        public List<string> DisabledMods { get; set; } = new List<string>();

        [JsonIgnore]
        public string FilePath { get; private set; }

        public bool IsDisabled(string modId)
        {
            if (DisabledMods == null || string.IsNullOrEmpty(modId)) return false;
            foreach (var id in DisabledMods)
                if (string.Equals(id, modId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void SetDisabled(string modId, bool disabled)
        {
            if (DisabledMods == null) DisabledMods = new List<string>();
            DisabledMods.RemoveAll(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase));
            if (disabled) DisabledMods.Add(modId);
        }

        public static LoaderConfig Load(string path, ModLogger logger)
        {
            LoaderConfig config = null;
            if (File.Exists(path))
            {
                try
                {
                    config = JsonConvert.DeserializeObject<LoaderConfig>(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    logger?.Warning("Could not parse " + path + " (" + e.Message + "); using defaults.");
                }
            }
            if (config == null)
            {
                config = new LoaderConfig();
                config.FilePath = path;
                config.Save(logger);
            }
            config.FilePath = path;
            if (config.DisabledMods == null) config.DisabledMods = new List<string>();
            return config;
        }

        public void Save(ModLogger logger = null)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                logger?.Exception(e, "Could not save " + FilePath);
            }
        }
    }
}
