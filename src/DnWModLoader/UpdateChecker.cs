using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DnWModLoader
{
    internal static class UpdateChecker
    {
        private const string ReleasesPage = "https://github.com/KrazenLabs/dnw-modloader/releases";
        private const string LatestReleaseApi = "https://api.github.com/repos/KrazenLabs/dnw-modloader/releases/latest";
        private const int TimeoutSeconds = 15;

        private static bool _checking;
        private static bool _checked;

        public static string Status { get; private set; } = "not checked";

        public static string LatestVersion { get; private set; }
        public static string ReleaseUrl { get; private set; }

        public static bool UpdateAvailable { get { return ReleaseUrl != null; } }

        public static void Begin()
        {
            if (_checking || _checked) return;
            _checking = true;
            Status = "checking...";
            try
            {
                var request = UnityWebRequest.Get(LatestReleaseApi);
                request.timeout = TimeoutSeconds;
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "DnWModLoader/" + ModLoader.Version);
                request.SendWebRequest().completed += _ => Finish(request);
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        public static void OpenReleasePage()
        {
            string url = ReleaseUrl ?? ReleasesPage;
            try { Application.OpenURL(url); }
            catch (Exception e) { ModLoader.Logger.Warning("Could not open " + url + ": " + e.Message); }
        }

        private static void Finish(UnityWebRequest request)
        {
            using (request)
            {
                try
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Fail(request.error);
                        return;
                    }

                    var release = JObject.Parse(request.downloadHandler.text);
                    string tag = (string)release["tag_name"];
                    if (!VersionUtil.TryParse(tag, out var latest))
                    {
                        Fail("unreadable release tag \"" + tag + "\"");
                        return;
                    }
                    _checking = false;
                    _checked = true;
                    if (VersionUtil.Compare(latest, ModLoader.ParsedVersion) <= 0)
                    {
                        Status = "up to date";
                        ModLoader.Logger.Debug("Loader is up to date (latest release: " + tag + ").");
                        return;
                    }

                    string url = (string)release["html_url"];
                    if (url == null || !url.StartsWith(ReleasesPage + "/", StringComparison.Ordinal)) url = ReleasesPage + "/tag/" + Uri.EscapeDataString(tag);
                    LatestVersion = latest.ToString();
                    ReleaseUrl = url;
                    Status = LatestVersion + " available";
                    ModLoader.Logger.Info("DnW Mod Loader " + LatestVersion + " is available (installed: " + ModLoader.Version + "): " + url);
                }
                catch (Exception e)
                {
                    Fail(e.Message);
                }
            }
        }

        private static void Fail(string reason)
        {
            _checking = false;
            Status = "check failed: " + reason;
            ModLoader.Logger.Debug("Update check failed: " + reason);
        }
    }
}
