using System;
using System.Collections.Generic;
using System.IO;
using DnWModLoader.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

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

        [JsonProperty("checkForUpdates")]
        public bool CheckForUpdates { get; set; } = true;

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
            if (!File.Exists(path))
            {
                var created = new LoaderConfig { FilePath = path };
                created.Save(logger);
                return created;
            }

            LoaderConfig config;
            try
            {
                config = ParseDocument(File.ReadAllText(path)).ToObject<LoaderConfig>(TolerantSerializer(path, logger));
            }
            catch (Exception e)
            {
                string backup = path + ".broken";
                bool copied;
                try { File.Copy(path, backup, true); copied = true; }
                catch { copied = false; }
                logger?.Warning("Could not read " + path + " (" + e.Message + "). Using default settings for this session"
                                + (copied ? "; old config file was copied to " + Path.GetFileName(backup) : "") + ".");
                config = new LoaderConfig();
            }
            config.FilePath = path;
            if (config.DisabledMods == null) config.DisabledMods = new List<string>();
            return config;
        }

        private static JObject ParseDocument(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new JsonException("the file is empty");
            using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
            {
                var document = JToken.ReadFrom(reader) as JObject;
                if (document == null) throw new JsonException("the file does not contain a JSON object");
                while (reader.Read())
                    if (reader.TokenType != JsonToken.Comment) throw new JsonException("unexpected text after the JSON object");
                return document;
            }
        }

        private static JsonSerializer TolerantSerializer(string path, ModLogger logger)
        {
            var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
            settings.Error = (sender, args) =>
            {
                if (args.CurrentObject == args.ErrorContext.OriginalObject)
                    logger?.Warning("Ignoring the invalid value of \"" + args.ErrorContext.Member + "\" in " + path + " (" + args.ErrorContext.Error.Message + "); using its default.");
                args.ErrorContext.Handled = true;
            };
            return JsonSerializer.Create(settings);
        }

        public void Save(ModLogger logger = null)
        {
            try
            {
                SafeFile.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (UnauthorizedAccessException e)
            {
                logger?.Warning("Could not save " + FilePath + ": " + e.Message);
            }
            catch (Exception e)
            {
                logger?.Exception(e, "Could not save " + FilePath);
            }
        }
    }
}
