using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using DnWModLoader.Logging;
using UnityEngine;

namespace DnWModLoader
{
    internal sealed class Overlay
    {
        private const int WindowId = 0x5D4E57;
        private const int SettingsTab = 0, ModsTab = 1, LogTab = 2;
        private const int MaxLogLines = 120;
        private static readonly string[] Tabs = { "Settings", "Mods", "Log" };
        private static readonly string[] LogFilters = { "All", "Info+", "Warnings+", "Errors" };
        private static readonly Color Invisible = new Color(1f, 1f, 1f, 0.004f);

        private readonly LoaderConfig _config;
        private readonly SettingsPanel _settings;
        private readonly OverlayHotkey _hotkey;
        private readonly OverlayOpenDelay _openDelay;
        private readonly OverlayWarmup _warmup = new OverlayWarmup(Tabs.Length);
        private readonly GameCursor _cursor = new GameCursor();
        private readonly float _bannerUntil;

        private bool _visible;
        private int _tab = SettingsTab;
        private int _logFilter = 1;
        private Vector2 _modsScroll;
        private Vector2 _logScroll;
        private Rect _windowRect = new Rect(40, 40, 900, 640);

        private bool _stylesReady;
        private GUIStyle _bannerStyle, _bannerShadowStyle, _errorStyle, _dimStyle, _logStyle, _boxStyle, _headerStyle, _windowStyle;
        private Texture2D _windowTexture, _boxTexture;

        public Overlay(LoaderConfig config)
        {
            _config = config ?? new LoaderConfig();
            _hotkey = new OverlayHotkey(_config.OverlayHotkey);
            _openDelay = new OverlayOpenDelay(() => _config.OverlayOpenDelayAfterSceneLoad);
            _settings = new SettingsPanel(_config, () => _hotkey.Set(_config.OverlayHotkey));
            _visible = _config.ShowOverlayOnStart;
            _bannerUntil = _config.ShowStartupBanner ? Time.realtimeSinceStartup + Mathf.Max(1f, _config.StartupBannerSeconds) : 0f;
        }

        public bool Visible
        {
            get { return _visible; }
            set
            {
                if (value && !_visible && _openDelay.Active)
                {
                    _openDelay.RequestOpen();
                    return;
                }
                _openDelay.CancelRequest();
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
                }
            }
        }

        public void Toggle()
        {
            if (_openDelay.OpenRequested) _openDelay.CancelRequest();
            else Visible = !Visible;
        }

        public void OpenSettings(string modId)
        {
            _tab = SettingsTab;
            if (!string.IsNullOrEmpty(modId)) _settings.FocusMod(modId);
            Visible = true;
        }

        public void NoteSceneLoaded(string sceneName)
        {
            _openDelay.NoteSceneLoaded();
            _warmup.NoteSceneLoaded(sceneName);
        }

        public void Update()
        {
            if (_hotkey.PressedThisFrame() && !HotkeyBlocked) Toggle();
            if (_openDelay.TakeDueRequest()) Visible = true;
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
                return;
            }
            if (_openDelay.OpenRequested)
                DrawBanner("DnW Mod Loader: overlay opens in " + _openDelay.SecondsRemaining.ToString("0.0") + " s (scene is still settling; press [" + _hotkey.Name + "] again to cancel)");
            else if (Time.realtimeSinceStartup < _bannerUntil)
                DrawBanner(StartupBannerText());
            int warmupFrame = _warmup.FrameDue(_visible);
            if (warmupFrame >= 0) DrawWarmupFrame(warmupFrame);
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
            DrawContents();
            GUI.DragWindow(new Rect(0, 0, 100000, 22));
        }

        private void DrawWarmupFrame(int frame)
        {
            var color = GUI.color;
            bool enabled = GUI.enabled;
            int tab = _tab;
            GUI.color = Invisible;
            GUI.enabled = false;
            _tab = frame;   // one frame per tab
            _settings.DrawAllRows = true;
            var rect = new Rect(_windowRect.x, _windowRect.y, Mathf.Min(_windowRect.width, Screen.width - 20), Mathf.Min(_windowRect.height, Screen.height - 20));
            GUILayout.BeginArea(rect, Title, _windowStyle);
            try
            {
                DrawContents();
            }
            finally
            {
                GUILayout.EndArea();
                _settings.DrawAllRows = false;
                _tab = tab;
                GUI.enabled = enabled;
                GUI.color = color;
            }
            if (Event.current.type != EventType.Repaint) return;
            _warmup.FrameRepainted();
            if (_warmup.Done)
                ModLoader.Logger.Debug("Overlay warmed up at frame " + Time.frameCount
                    + " (settings rows " + _settings.RowsDrawn + " of " + _settings.RowsTotal + ", content " + _settings.ContentHeight.ToString("0") + " px).");
        }

        private void DrawContents()
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
                case SettingsTab: _settings.Draw(_windowRect.height - 130f); break;
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
            if (info != null && !string.IsNullOrEmpty(info.Author)) title += "   by " + info.Author;
            GUILayout.Label(title, mod.Status == ModStatus.Failed || mod.Status == ModStatus.Skipped ? _errorStyle : _headerStyle);
            GUILayout.FlexibleSpace();
            if (mod.Status == ModStatus.Loaded && mod.Instance?.Config != null && mod.Instance.Config.Entries.Count > 0)
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

            var entries = Log.GetRecent();
            var lines = new List<string>(MaxLogLines);
            int matching = 0;
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                if (entries[i].Level < min) continue;
                matching++;
                if (lines.Count < MaxLogLines) lines.Add(entries[i].ToString());
            }
            lines.Reverse();

            var text = new StringBuilder();
            if (matching > lines.Count) text.Append("(").Append(matching - lines.Count).Append(" older lines omitted here; the full log is in ModLoader.log)\n");
            foreach (var line in lines) text.Append(line).Append('\n');
            if (lines.Count == 0) text.Append("(nothing logged at this level yet)");

            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
            GUILayout.Label(text.ToString(), _logStyle);
            GUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ banners

        private string StartupBannerText()
        {
            ModLoader.CountStatuses(out int loaded, out int failed, out int skipped, out int disabled);
            string text = Title + " - " + loaded + " mod" + (loaded == 1 ? "" : "s") + " loaded";
            if (failed > 0) text += ", " + failed + " failed";
            if (skipped > 0) text += ", " + skipped + " skipped";
            if (disabled > 0) text += ", " + disabled + " disabled";
            return text + "   [" + _hotkey.Name + "] settings";
        }

        private void DrawBanner(string text)
        {
            var rect = new Rect(12, 8, Screen.width - 24, 24);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, _bannerShadowStyle);
            GUI.Label(rect, text, _bannerStyle);
        }

        // ------------------------------------------------------------------ actions

        private static void ReloadConfigs()
        {
            foreach (var mod in ModLoader.Mods)
            {
                if (mod.Status != ModStatus.Loaded || mod.Instance == null) continue;
                try { mod.Instance.Config.Reload(); }
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

        // ------------------------------------------------------------------ styles

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
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}
