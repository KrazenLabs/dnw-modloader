using System;
using System.Diagnostics;
using System.Text;
using DnWModLoader.Logging;
using UnityEngine;

namespace DnWModLoader
{
    internal sealed class Overlay
    {
        private const int WindowId = 0x5D4E57;
        private const int UpdateNoticeId = WindowId + 1;
        private const int SettingsTab = 0, ModsTab = 1, LogTab = 2;
        private static readonly string[] Tabs = { "Settings", "Mods", "Log" };
        private static readonly string[] LogFilters = { "All", "Info+", "Warnings+", "Errors" };

        private enum NoticeState { Pending, Showing, Done }

        private readonly LoaderConfig _config;
        private readonly SettingsPanel _settings;
        private readonly OverlayHotkey _hotkey;
        private readonly GameCursor _cursor = new GameCursor();
        private readonly float _bannerUntil;
        private readonly bool _showDirect3D12Warning;
        private readonly bool _showParallelLoaderWarning;

        private bool _visible;
        private int _tab = SettingsTab;
        private int _logFilter = 1;
        private Vector2 _modsScroll;
        private Vector2 _logScroll;
        private Rect _windowRect = new Rect(40, 40, 900, 640);
        private NoticeState _updateNotice;

        private bool _stylesReady;
        private GUIStyle _bannerStyle, _bannerShadowStyle, _warningBannerStyle, _errorStyle, _dimStyle, _logStyle, _boxStyle, _headerStyle, _windowStyle, _noticeStyle, _linkStyle;
        private Texture2D _windowTexture, _boxTexture, _noticeTexture;

        public Overlay(LoaderConfig config)
        {
            _config = config ?? new LoaderConfig();
            _hotkey = new OverlayHotkey(_config.OverlayHotkey);
            _settings = new SettingsPanel(_config, () => _hotkey.Set(_config.OverlayHotkey));
            _visible = _config.ShowOverlayOnStart;
            _bannerUntil = _config.ShowStartupBanner ? Time.realtimeSinceStartup + Mathf.Max(1f, _config.StartupBannerSeconds) : 0f;
            _showDirect3D12Warning = Direct3D12Warning.Applies();
            _showParallelLoaderWarning = ParallelLoaderWarning.Applies();
        }

        public bool Visible
        {
            get { return _visible; }
            set
            {
                if (_visible == value) return;
                _visible = value;
                if (_visible)
                {
                    _cursor.Unlock();
                }
                else
                {
                    _cursor.Restore();
                    GUI.FocusControl(null);
                    if (_updateNotice == NoticeState.Showing) _updateNotice = NoticeState.Done;
                }
            }
        }

        public void Toggle()
        {
            Visible = !Visible;
        }

        public void OpenSettings(string modId)
        {
            _tab = SettingsTab;
            if (!string.IsNullOrEmpty(modId)) _settings.FocusMod(modId);
            Visible = true;
        }

        public void Update()
        {
            if (_hotkey.PressedThisFrame() && !HotkeyBlocked) Toggle();
            _cursor.KeepUnlocked();
        }

        public void LateUpdate()
        {
            _cursor.KeepUnlocked();
        }

        public void OnGUI()
        {
            EnsureStyles();
            GUI.depth = -1000;
            if (_hotkey.IsKeyDown(Event.current) && !HotkeyBlocked)
            {
                Toggle();
                Event.current.Use();
            }

            if (_visible)
            {
                DrawWindow();
                DrawUpdateNotice();
            }
            else if (Time.realtimeSinceStartup < _bannerUntil) DrawStartupBanner();
        }

        private bool HotkeyBlocked
        {
            get { return _visible && _settings.TextFieldFocused; }
        }

        private string Title
        {
            get { return "DnW Mod Loader " + ModLoader.Version; }
        }

        private void DrawWindow()
        {
            _windowRect.width = Mathf.Min(_windowRect.width, Screen.width - 20);
            _windowRect.height = Mathf.Min(_windowRect.height, Screen.height - 20);
            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindowContents, Title, _windowStyle);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Mathf.Max(0, Screen.width - _windowRect.width));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Mathf.Max(0, Screen.height - _windowRect.height));
        }

        private void DrawWindowContents(int id)
        {
            GUILayout.BeginHorizontal();
            ModLoader.CountStatuses(out int loaded, out int failed, out int skipped, out int disabled);
            Tabs[ModsTab] = "Mods (" + ModLoader.Mods.Count + ")";
            _tab = GUILayout.Toolbar(_tab, Tabs, GUILayout.Width(300));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close [" + _hotkey.Name + "]", GUILayout.Width(110))) Visible = false;
            GUILayout.EndHorizontal();

            string game;
            try { game = Application.productName + " " + Application.version + " | Unity " + Application.unityVersion; }
            catch { game = "?"; }
            GUILayout.Label(game + " | " + loaded + " loaded, " + failed + " failed, " + skipped + " skipped, " + disabled + " disabled", _dimStyle);

            switch (_tab)
            {
                case SettingsTab: _settings.Draw(); break;
                case ModsTab: DrawMods(); break;
                default: DrawLog(); break;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Mods folder")) OpenPath(ModLoader.ModsDirectory);
            if (GUILayout.Button("Open log")) OpenPath(ModLoader.LogFilePath ?? ModLoader.ModsDirectory);
            if (GUILayout.Button("Reload mod configs")) ReloadConfigs();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Log: " + (ModLoader.LogFilePath ?? "(no file)"), _dimStyle);
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 100000, 22));
        }

        private void DrawUpdateNotice()
        {
            if (_updateNotice == NoticeState.Pending && UpdateChecker.UpdateAvailable) _updateNotice = NoticeState.Showing;
            if (_updateNotice != NoticeState.Showing) return;

            float width = Mathf.Min(560f, Screen.width - 20f), height = 132f;
            float x = Mathf.Clamp(_windowRect.center.x - width / 2f, 0f, Mathf.Max(0f, Screen.width - width));
            float y = Mathf.Clamp(_windowRect.center.y - height / 2f, 0f, Mathf.Max(0f, Screen.height - height));
            GUI.ModalWindow(UpdateNoticeId, new Rect(x, y, width, height), DrawUpdateNoticeContents, "Update available", _noticeStyle);
        }

        private void DrawUpdateNoticeContents(int id)
        {
            GUILayout.Label("A newer DnW Mod Loader version (" + UpdateChecker.LatestVersion + ") is available.", _headerStyle);
            GUILayout.Label("Please download the update from the release page.", _dimStyle);
            GUILayout.Space(4);
            if (Link(UpdateChecker.ReleaseUrl))
            {
                UpdateChecker.OpenReleasePage();
                _updateNotice = NoticeState.Done;
            }
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(80))) _updateNotice = NoticeState.Done;
            GUILayout.EndHorizontal();
        }

        private bool Link(string text)
        {
            var content = new GUIContent(text);
            var rect = GUILayoutUtility.GetRect(content, _linkStyle, GUILayout.ExpandWidth(false));
            var previous = GUI.color;
            GUI.color = rect.Contains(Event.current.mousePosition) ? new Color(0.75f, 0.87f, 1f) : new Color(0.45f, 0.7f, 1f);
            bool clicked = GUI.Button(rect, content, _linkStyle);
            GUI.DrawTexture(new Rect(rect.x + _linkStyle.padding.left, rect.yMax - _linkStyle.padding.bottom, rect.width - _linkStyle.padding.horizontal, 1f), Texture2D.whiteTexture);
            GUI.color = previous;
            return clicked;
        }

        private void DrawMods()
        {
            _modsScroll = GUILayout.BeginScrollView(_modsScroll, GUILayout.ExpandHeight(true));
            if (ModLoader.Mods.Count == 0)
                GUILayout.Label("No mods found. Put mod folders into:\n" + ModLoader.ModsDirectory, _dimStyle);
            foreach (var mod in ModLoader.Mods) DrawModCard(mod);
            GUILayout.EndScrollView();
        }

        private void DrawModCard(ModContainer mod)
        {
            var info = mod.Info;
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.BeginHorizontal();
            string title = (info != null ? info.Name + "  " + info.VersionString : "?") + "   [" + mod.Status + "]";
            if (mod.Framework != null) title += "   " + mod.Framework + " plugin";
            if (info != null && !string.IsNullOrEmpty(info.Author)) title += "   by " + info.Author;
            GUILayout.Label(title, mod.Status == ModStatus.Failed || mod.Status == ModStatus.Skipped ? _errorStyle : _headerStyle);
            GUILayout.FlexibleSpace();
            if (mod.Status == ModStatus.Loaded && mod.Settings != null && mod.Settings.HasEntries)
            {
                if (GUILayout.Button("Settings", GUILayout.Width(70))) OpenSettings(info.Id);
            }
            if (info != null && mod.Status != ModStatus.Failed)
            {
                bool disabled = _config.IsDisabled(info.Id);
                if (GUILayout.Button(disabled ? "Enable (next launch)" : "Disable (next launch)", GUILayout.Width(150)))
                {
                    _config.SetDisabled(info.Id, !disabled);
                    _config.Save(ModLoader.Logger);
                    ModLoader.Logger.Info("Mod " + info.Id + (disabled ? " enabled" : " disabled") + " for the next game launch.");
                }
            }
            GUILayout.EndHorizontal();

            if (info != null)
            {
                string details = "id: " + info.Id + "   file: " + (info.AssemblyPath != null ? System.IO.Path.GetFileName(info.AssemblyPath) : "?");
                if (mod.Status == ModStatus.Loaded) details += "   patches: " + mod.PatchedMethodCount + "   init: " + mod.InitializeMilliseconds.ToString("0") + " ms";
                GUILayout.Label(details, _dimStyle);
                if (!string.IsNullOrEmpty(info.Description)) GUILayout.Label(info.Description, _dimStyle);
            }
            if (!string.IsNullOrEmpty(mod.Error)) GUILayout.Label(mod.Error, _errorStyle);
            GUILayout.EndVertical();
        }

        private void DrawLog()
        {
            _logFilter = GUILayout.Toolbar(_logFilter, LogFilters, GUILayout.Width(320));
            LogLevel min = _logFilter == 0 ? LogLevel.Debug : _logFilter == 1 ? LogLevel.Info : _logFilter == 2 ? LogLevel.Warning : LogLevel.Error;

            var text = new StringBuilder();
            foreach (var entry in Log.GetRecent())
                if (entry.Level >= min) text.Append(entry).Append('\n');
            if (text.Length == 0) text.Append("(nothing logged at this level yet)");

            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
            GUILayout.Label(text.ToString(), _logStyle);
            GUILayout.EndScrollView();
        }

        private string StartupBannerText()
        {
            ModLoader.CountStatuses(out int loaded, out int failed, out int skipped, out int disabled);
            string text = Title + " - " + loaded + " mod" + (loaded == 1 ? "" : "s") + " loaded";
            if (failed > 0) text += ", " + failed + " failed";
            if (skipped > 0) text += ", " + skipped + " skipped";
            if (disabled > 0) text += ", " + disabled + " disabled";
            return text + "   [" + _hotkey.Name + "] settings";
        }

        private void DrawStartupBanner()
        {
            DrawBanner(0, StartupBannerText(), _bannerStyle);
            int line = 1;
            if (_showDirect3D12Warning) DrawBanner(line++, Direct3D12Warning.Banner, _warningBannerStyle);
            if (_showParallelLoaderWarning) DrawBanner(line, ParallelLoaderWarning.Banner, _warningBannerStyle);
        }

        private void DrawBanner(int line, string text, GUIStyle style)
        {
            var rect = new Rect(12, 8 + line * 22, Screen.width - 24, 24);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, _bannerShadowStyle);
            GUI.Label(rect, text, style);
        }

        private static void ReloadConfigs()
        {
            foreach (var mod in ModLoader.Mods)
            {
                if (mod.Status != ModStatus.Loaded || mod.Settings == null) continue;
                try { mod.Settings.Reload(); }
                catch (Exception e) { ModLoader.Logger.Exception(e, "Reloading config of " + mod.Info.Id + " failed"); }
            }
            ModLoader.Logger.Info("Mod configs reloaded.");
        }

        private static void OpenPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception e) { ModLoader.Logger.Warning("Could not open " + path + ": " + e.Message); }
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;
            _windowTexture = MakeTexture(new Color(0.09f, 0.09f, 0.11f, 0.96f));
            _boxTexture = MakeTexture(new Color(0.16f, 0.16f, 0.19f, 0.96f));
            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _windowTexture;
            _windowStyle.onNormal.background = _windowTexture;
            _windowStyle.normal.textColor = Color.white;
            _windowStyle.onNormal.textColor = Color.white;
            _bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            _bannerStyle.normal.textColor = new Color(1f, 0.92f, 0.55f);
            _bannerShadowStyle = new GUIStyle(_bannerStyle);
            _bannerShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            _warningBannerStyle = new GUIStyle(_bannerStyle);
            _warningBannerStyle.normal.textColor = new Color(1f, 0.6f, 0.45f);
            _errorStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _errorStyle.normal.textColor = new Color(1f, 0.45f, 0.4f);
            _dimStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _dimStyle.normal.textColor = new Color(0.82f, 0.82f, 0.85f);
            _logStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false, fontSize = 11 };
            _logStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            _boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };
            _boxStyle.normal.background = _boxTexture;
            _headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _headerStyle.normal.textColor = Color.white;
            _noticeTexture = MakeFramedTexture(new Color(0.13f, 0.13f, 0.16f, 0.98f), new Color(1f, 0.8f, 0.35f));
            _noticeStyle = new GUIStyle(_windowStyle) { border = new RectOffset(1, 1, 1, 1), overflow = new RectOffset(), fontStyle = FontStyle.Bold };
            _noticeStyle.normal.background = _noticeTexture;
            _noticeStyle.onNormal.background = _noticeTexture;
            _noticeStyle.normal.textColor = _bannerStyle.normal.textColor;
            _noticeStyle.onNormal.textColor = _bannerStyle.normal.textColor;
            // Tinted with GUI.color
            _linkStyle = new GUIStyle(GUI.skin.label) { wordWrap = false };
            _linkStyle.normal.textColor = Color.white;
            _linkStyle.hover.textColor = Color.white;
            _linkStyle.active.textColor = Color.white;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        // 3x3 texture with a one pixel frame, for styles with a border of 1
        private static Texture2D MakeFramedTexture(Color fill, Color frame)
        {
            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    texture.SetPixel(x, y, x == 1 && y == 1 ? fill : frame);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}
