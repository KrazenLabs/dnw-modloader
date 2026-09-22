using System;
using System.Collections.Generic;
using System.Globalization;
using DnWModLoader.Config;
using DnWModLoader.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DnWModLoader
{
    internal sealed class SettingsPanel
    {
        private enum Widget { Toggle, Choice, Slider, Text, Key }

        private const string LoaderId = "__loader";
        private const string ControlPrefix = "dnw.settings.";
        private const string HotkeyPickerId = ControlPrefix + "loader.hotkey";
        private const string KeyboardShortcutTypeName = "BepInEx.Configuration.KeyboardShortcut";
        private const float LabelWidth = 190f;
        private const float ValueFieldWidth = 80f;
        private const float KeyWidth = 170f;
        private const float ResetWidth = 52f;
        private const float ErrorSeconds = 6f;

        private readonly LoaderConfig _loaderConfig;
        private readonly Action _onLoaderConfigChanged;
        private readonly KeyPicker _picker = new KeyPicker();
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();
        private readonly Dictionary<string, string> _editBuffers = new Dictionary<string, string>();
        private readonly Dictionary<string, KeyValuePair<string, float>> _errors = new Dictionary<string, KeyValuePair<string, float>>();

        private string _search = "";
        private bool _showDescriptions = true;
        private bool _showAdvanced;
        private Vector2 _scroll;
        private string _focusedName = "";

        private GUIStyle _header, _dim, _error, _box, _sectionHeader, _small;
        private Texture2D _boxTexture;

        public SettingsPanel(LoaderConfig loaderConfig, Action onLoaderConfigChanged)
        {
            _loaderConfig = loaderConfig;
            _onLoaderConfigChanged = onLoaderConfigChanged;
        }

        public bool TextFieldFocused { get; private set; }

        public bool PickingKey { get { return _picker.Listening; } }

        public void CancelKeyPick()
        {
            _picker.Cancel();
        }

        // Expands one mod, collapses the others
        public void FocusMod(string modId)
        {
            SetAllExpanded(false);
            _expanded[modId] = true;
            _search = "";
            _scroll = Vector2.zero;
        }

        public void Draw()
        {
            EnsureStyles();
            _focusedName = GUI.GetNameOfFocusedControl() ?? "";
            TextFieldFocused = _focusedName.StartsWith(ControlPrefix, StringComparison.Ordinal);

            DrawToolbar();
            string filter = _search.Trim();
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            int shown = DrawLoaderSettings(filter) ? 1 : 0;
            foreach (var mod in ModLoader.Mods)
                if (DrawMod(mod, filter)) shown++;
            if (shown == 0)
            {
                GUILayout.Space(10);
                GUILayout.Label(filter.Length > 0 ? "Nothing matches \"" + filter + "\"." : "No mod has registered settings yet.", _dim);
            }
            GUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(50));
            GUI.SetNextControlName(ControlPrefix + "search");
            _search = GUILayout.TextField(_search, GUILayout.Width(220));
            if (GUILayout.Button("x", GUILayout.Width(22)))
            {
                GUI.FocusControl(null);
                _search = "";
            }
            GUILayout.Space(10);
            _showDescriptions = GUILayout.Toggle(_showDescriptions, "Descriptions", GUILayout.Width(100));
            _showAdvanced = GUILayout.Toggle(_showAdvanced, "Advanced", GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Expand all", GUILayout.Width(80))) SetAllExpanded(true);
            if (GUILayout.Button("Collapse all", GUILayout.Width(85))) SetAllExpanded(false);
            GUILayout.EndHorizontal();
        }

        private bool DrawMod(ModContainer mod, string filter)
        {
            if (mod.Status != ModStatus.Loaded || mod.Settings == null) return false;
            var config = mod.Settings;
            var sections = VisibleSections(config, mod.Info, filter);
            if (sections.Count == 0) return false;

            string modId = mod.Info.Id;
            bool filtering = filter.Length > 0;
            bool expanded = filtering || IsExpanded(modId, false);

            GUILayout.BeginVertical(_box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button((expanded ? "v  " : ">  ") + mod.Info.Name + "  " + mod.Info.VersionString, _header, GUILayout.ExpandWidth(true)) && !filtering)
                _expanded[modId] = !expanded;
            GUILayout.Label(config.HasPendingChanges ? "saving..." : "", _small, GUILayout.Width(60));
            if (GUILayout.Button("Reset all", GUILayout.Width(70))) config.ResetAll();
            GUILayout.EndHorizontal();

            if (expanded)
                foreach (var section in sections) DrawSection(modId, config, section.Key, section.Value);
            GUILayout.EndVertical();
            return true;
        }

        private List<KeyValuePair<string, List<ConfigEntryBase>>> VisibleSections(ISettingsSource config, ModInfo info, string filter)
        {
            var result = new List<KeyValuePair<string, List<ConfigEntryBase>>>();
            bool filtering = filter.Length > 0;
            bool modMatches = filtering && Matches(filter, info.Name, info.Id);
            foreach (var section in config.EntriesBySection())
            {
                var sectionInfo = config.GetSectionInfo(section.Key);
                if (sectionInfo != null && sectionInfo.Advanced && !_showAdvanced && !filtering) continue;
                bool sectionMatches = modMatches || (filtering && Matches(filter, section.Key, sectionInfo?.DisplayName, sectionInfo?.Description));
                var entries = new List<ConfigEntryBase>();
                foreach (var entry in section.Value)
                {
                    if (entry.Meta.Hidden) continue;
                    if (entry.Meta.Advanced && !_showAdvanced && !filtering) continue;
                    if (filtering && !sectionMatches && !Matches(filter, entry.Key, entry.DisplayName, entry.Description)) continue;
                    entries.Add(entry);
                }
                if (entries.Count > 0) result.Add(new KeyValuePair<string, List<ConfigEntryBase>>(section.Key, entries));
            }
            return result;
        }

        private void DrawSection(string modId, ISettingsSource config, string sectionKey, List<ConfigEntryBase> entries)
        {
            var info = config.GetSectionInfo(sectionKey);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label(!string.IsNullOrEmpty(info?.DisplayName) ? info.DisplayName : sectionKey, _sectionHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset", GUILayout.Width(ResetWidth))) config.ResetSection(sectionKey);
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(info?.Description)) GUILayout.Label(info.Description, _dim);

            foreach (var entry in entries)
                DrawEntry(entry, ControlPrefix + modId + "." + entry.Section + "." + entry.Key);
        }

        private void DrawEntry(ConfigEntryBase entry, string controlId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(entry.DisplayName + (entry.Meta.RequiresRestart ? " *" : ""), GUILayout.Width(LabelWidth));
            try
            {
                switch (WidgetFor(entry))
                {
                    case Widget.Toggle: DrawToggle(entry); break;
                    case Widget.Choice: DrawChoice(entry, Choices(entry)); break;
                    case Widget.Slider: DrawSlider(entry, controlId); break;
                    case Widget.Key: DrawKey(entry, controlId); break;
                    default: DrawValueField(entry, controlId, 0f); break;
                }
            }
            catch (Exception e)
            {
                GUILayout.Label("error: " + e.Message, _error);
            }
            GUI.enabled = !entry.IsDefault;
            if (GUILayout.Button("Reset", GUILayout.Width(ResetWidth)))
            {
                entry.Reset();
                _editBuffers.Remove(controlId);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (_errors.TryGetValue(controlId, out var error))
            {
                if (Event.current.type == EventType.Layout && Time.realtimeSinceStartup - error.Value > ErrorSeconds) _errors.Remove(controlId);
                else GUILayout.Label(error.Key, _error);
            }
            if (_showDescriptions && !string.IsNullOrEmpty(entry.Description))
                GUILayout.Label(entry.Description + (entry.Meta.RequiresRestart ? "  (* takes effect after a restart)" : ""), _dim);
        }

        private static Widget WidgetFor(ConfigEntryBase entry)
        {
            var type = ValueType(entry);
            if (type == typeof(bool)) return Widget.Toggle;
            if (IsKeyBinding(entry, type)) return Widget.Key;
            // Flag combinations are typed as text, e.g. "Warning, Error"
            if ((type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false)) || (entry.Meta.AcceptableValues != null && entry.Meta.AcceptableValues.Length > 0)) return Widget.Choice;
            if (ConfigEntryBase.IsNumericType(type) && entry.Meta.HasRange) return Widget.Slider;
            return Widget.Text;
        }

        private static Type ValueType(ConfigEntryBase entry)
        {
            return Nullable.GetUnderlyingType(entry.ValueType) ?? entry.ValueType;
        }

        private static bool IsKeyBinding(ConfigEntryBase entry, Type type)
        {
            if (type == typeof(Key) || type == typeof(KeyCode) || type.FullName == KeyboardShortcutTypeName) return true;
            return entry.Meta.KeyBinding && (type == typeof(string) || type.IsEnum);
        }

        private void DrawKey(ConfigEntryBase entry, string controlId)
        {
            if (DrawKeyPicker(controlId, KeyLabel(entry), out Key key)) AssignKey(entry, key, controlId);
        }

        private bool DrawKeyPicker(string id, string label, out Key key)
        {
            bool picked = _picker.Draw(id, label, KeyWidth, out key);
            GUILayout.Label(_picker.IsListening(id) ? "Esc cancels" : "", _small, GUILayout.ExpandWidth(true));
            return picked;
        }

        private static string KeyLabel(ConfigEntryBase entry)
        {
            object value = entry.BoxedValue;
            if (value is Key key) return KeyNames.Label(key);
            if (value is KeyCode code) return KeyNames.Label(code);
            return KeyNames.Label(entry.ValueToDisplayString());
        }

        private void AssignKey(ConfigEntryBase entry, Key key, string controlId)
        {
            var type = ValueType(entry);
            bool assigned;
            if (type == typeof(Key)) assigned = TryAssign(entry, key);
            else if (type == typeof(KeyCode)) assigned = KeyNames.TryToKeyCode(key, out var code) && TryAssign(entry, code);
            else if (type.FullName == KeyboardShortcutTypeName) assigned = KeyNames.TryToKeyCode(key, out var mainKey) && entry.TrySetFromString(mainKey.ToString(), out _);
            else assigned = Acceptable(entry, key.ToString()) && entry.TrySetFromString(key.ToString(), out _);

            if (assigned) _errors.Remove(controlId);
            else _errors[controlId] = new KeyValuePair<string, float>(KeyNames.Label(key) + " cannot be used for this setting.", Time.realtimeSinceStartup);
        }

        private static bool TryAssign(ConfigEntryBase entry, object value)
        {
            if (!Acceptable(entry, value)) return false;
            entry.BoxedValue = value;
            return true;
        }

        private static bool Acceptable(ConfigEntryBase entry, object value)
        {
            var values = entry.Meta.AcceptableValues;
            if (values == null || values.Length == 0) return true;
            foreach (var candidate in values)
                if (Equals(candidate, value) || (candidate != null && value != null && candidate.ToString() == value.ToString())) return true;
            return false;
        }

        private static Array Choices(ConfigEntryBase entry)
        {
            var type = ValueType(entry);
            if (entry.Meta.AcceptableValues != null && entry.Meta.AcceptableValues.Length > 0) return entry.Meta.AcceptableValues;
            return Enum.GetValues(type);
        }

        private static void DrawToggle(ConfigEntryBase entry)
        {
            bool current = (bool)entry.BoxedValue;
            bool next = GUILayout.Toggle(current, current ? " on" : " off", GUILayout.ExpandWidth(true));
            if (next != current) entry.BoxedValue = next;
        }

        private static void DrawChoice(ConfigEntryBase entry, Array values)
        {
            object current = entry.BoxedValue;
            int index = -1;
            for (int i = 0; i < values.Length; i++)
            {
                var candidate = values.GetValue(i);
                if (Equals(candidate, current) || (candidate != null && current != null && candidate.ToString() == current.ToString())) { index = i; break; }
            }
            if (GUILayout.Button("<", GUILayout.Width(26)) && values.Length > 0) entry.BoxedValue = values.GetValue((index - 1 + values.Length) % values.Length);
            GUILayout.Label(current != null ? current.ToString() : "(null)", GUILayout.Width(170));
            if (GUILayout.Button(">", GUILayout.Width(26)) && values.Length > 0) entry.BoxedValue = values.GetValue((index + 1) % values.Length);
            GUILayout.Label(values.Length > 1 ? (index + 1) + " / " + values.Length : "", GUILayout.ExpandWidth(true));
        }

        private void DrawSlider(ConfigEntryBase entry, string controlId)
        {
            var type = ValueType(entry);
            double min = entry.Meta.Min.Value, max = entry.Meta.Max.Value;
            double current = Convert.ToDouble(entry.BoxedValue, CultureInfo.InvariantCulture);
            double next = GUILayout.HorizontalSlider((float)current, (float)min, (float)max, GUILayout.ExpandWidth(true));
            double step = entry.Meta.Step;
            if (ConfigEntryBase.IsIntegerType(type) && step <= 0) step = 1;
            if (step > 0) next = Math.Round((next - min) / step) * step + min;
            next = Math.Max(min, Math.Min(max, next));
            if (Math.Abs(next - current) > 1e-6)
            {
                entry.BoxedValue = Convert.ChangeType(next, type, CultureInfo.InvariantCulture);
                _editBuffers.Remove(controlId);
            }
            DrawValueField(entry, controlId, ValueFieldWidth);
        }

        private void DrawValueField(ConfigEntryBase entry, string controlId, float width)
        {
            bool focused = _focusedName == controlId;
            string value = entry.ValueToDisplayString();
            if (!focused && _editBuffers.TryGetValue(controlId, out var pending))
            {
                _editBuffers.Remove(controlId);
                if (pending != value) Commit(entry, controlId, pending);
                value = entry.ValueToDisplayString();
            }

            string shown = focused && _editBuffers.TryGetValue(controlId, out var buffer) ? buffer : value;
            GUI.SetNextControlName(controlId);
            string edited = width > 0f ? GUILayout.TextField(shown, GUILayout.Width(width)) : GUILayout.TextField(shown, GUILayout.ExpandWidth(true));
            if (!focused) return;

            var e = Event.current;
            bool keyDown = e.type == EventType.KeyDown;
            if (keyDown && e.keyCode == KeyCode.Escape)
            {
                _editBuffers.Remove(controlId);
                GUI.FocusControl(null);
                e.Use();
                return;
            }
            if (edited != shown || _editBuffers.ContainsKey(controlId)) _editBuffers[controlId] = edited;
            if (keyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                _editBuffers.Remove(controlId);
                Commit(entry, controlId, edited);
                GUI.FocusControl(null);
                e.Use();
            }
        }

        private void Commit(ConfigEntryBase entry, string controlId, string text)
        {
            if (entry.TrySetFromString(text, out string error)) _errors.Remove(controlId);
            else _errors[controlId] = new KeyValuePair<string, float>("Invalid value: " + error, Time.realtimeSinceStartup);
        }

        private bool DrawLoaderSettings(string filter)
        {
            bool filtering = filter.Length > 0;
            if (filtering && !Matches(filter, "Mod Loader", "loader", "overlay", "hotkey", "log", "banner", "updates")) return false;
            bool expanded = filtering || IsExpanded(LoaderId, false);

            GUILayout.BeginVertical(_box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button((expanded ? "v  " : ">  ") + "DnW Mod Loader  " + ModLoader.Version, _header, GUILayout.ExpandWidth(true)) && !filtering)
                _expanded[LoaderId] = !expanded;
            GUILayout.EndHorizontal();
            if (expanded) DrawLoaderEntries();
            GUILayout.EndVertical();
            return true;
        }

        private void DrawLoaderEntries()
        {
            var c = _loaderConfig;
            bool changed = false;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Overlay hotkey", GUILayout.Width(LabelWidth));
            if (DrawKeyPicker(HotkeyPickerId, KeyNames.Label(c.OverlayHotkey), out Key hotkey) && hotkey.ToString() != c.OverlayHotkey)
            {
                c.OverlayHotkey = hotkey.ToString();
                changed = true;
            }
            GUILayout.EndHorizontal();
            if (_showDescriptions) GUILayout.Label("Opens and closes this window.", _dim);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Startup banner", GUILayout.Width(LabelWidth));
            bool banner = GUILayout.Toggle(c.ShowStartupBanner, c.ShowStartupBanner ? " on" : " off", GUILayout.Width(60));
            if (banner != c.ShowStartupBanner) { c.ShowStartupBanner = banner; changed = true; }
            GUILayout.Label("seconds", GUILayout.Width(60));
            float seconds = Mathf.Round(GUILayout.HorizontalSlider(c.StartupBannerSeconds, 1f, 60f, GUILayout.Width(160)));
            if (Math.Abs(seconds - c.StartupBannerSeconds) > 0.5f) { c.StartupBannerSeconds = seconds; changed = true; }
            GUILayout.Label(c.StartupBannerSeconds.ToString("0"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Open overlay on start", GUILayout.Width(LabelWidth));
            bool onStart = GUILayout.Toggle(c.ShowOverlayOnStart, c.ShowOverlayOnStart ? " on" : " off", GUILayout.Width(60));
            if (onStart != c.ShowOverlayOnStart) { c.ShowOverlayOnStart = onStart; changed = true; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Check for updates", GUILayout.Width(LabelWidth));
            bool updates = GUILayout.Toggle(c.CheckForUpdates, c.CheckForUpdates ? " on" : " off", GUILayout.Width(60));
            if (updates != c.CheckForUpdates)
            {
                c.CheckForUpdates = updates;
                changed = true;
                if (updates) UpdateChecker.Begin();
            }
            GUILayout.Label(UpdateChecker.Status, _small, GUILayout.ExpandWidth(false));
            if (UpdateChecker.UpdateAvailable && GUILayout.Button("Release page", GUILayout.Width(95))) UpdateChecker.OpenReleasePage();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Log level", GUILayout.Width(LabelWidth));
            var level = CycleEnum(c.LogLevel, new[] { LogLevel.Debug, LogLevel.Info, LogLevel.Warning, LogLevel.Error });
            if (level != c.LogLevel) { c.LogLevel = level; Log.MinimumLevel = level; changed = true; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Mirror Unity log", GUILayout.Width(LabelWidth));
            var mirror = CycleEnum(c.MirrorUnityLog, (UnityLogMirror[])Enum.GetValues(typeof(UnityLogMirror)));
            if (mirror != c.MirrorUnityLog) { c.MirrorUnityLog = mirror; changed = true; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Echo loader log to Player.log", GUILayout.Width(LabelWidth));
            bool echo = GUILayout.Toggle(c.EchoLoaderLogToUnity, c.EchoLoaderLogToUnity ? " on" : " off", GUILayout.Width(60));
            if (echo != c.EchoLoaderLogToUnity) { c.EchoLoaderLogToUnity = echo; Log.EchoToUnity = echo; changed = true; }
            GUILayout.EndHorizontal();

            if (_showDescriptions)
                GUILayout.Label("Stored in Mods/ModLoader.json. Disabled mods are managed on the Mods tab.", _dim);

            if (changed)
            {
                c.Save(ModLoader.Logger);
                try { _onLoaderConfigChanged?.Invoke(); }
                catch (Exception e) { ModLoader.Logger.Debug("Loader config change hook failed: " + e.Message); }
            }
        }

        private static T CycleEnum<T>(T current, T[] values) where T : struct
        {
            int index = Array.IndexOf(values, current);
            T result = current;
            if (GUILayout.Button("<", GUILayout.Width(26))) result = values[(index - 1 + values.Length) % values.Length];
            GUILayout.Label(current.ToString(), GUILayout.Width(90));
            if (GUILayout.Button(">", GUILayout.Width(26))) result = values[(index + 1) % values.Length];
            return result;
        }

        private bool IsExpanded(string id, bool defaultValue)
        {
            return _expanded.TryGetValue(id, out bool value) ? value : defaultValue;
        }

        private void SetAllExpanded(bool value)
        {
            _expanded[LoaderId] = value;
            foreach (var mod in ModLoader.Mods)
                if (mod.Info != null) _expanded[mod.Info.Id] = value;
        }

        private static bool Matches(string filter, params string[] texts)
        {
            foreach (var text in texts)
                if (!string.IsNullOrEmpty(text) && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void EnsureStyles()
        {
            if (_header != null) return;
            _boxTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _boxTexture.SetPixel(0, 0, new Color(0.16f, 0.16f, 0.19f, 0.96f));
            _boxTexture.Apply();
            _boxTexture.hideFlags = HideFlags.HideAndDontSave;
            _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft };
            _box.normal.background = _boxTexture;
            _header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _header.normal.textColor = Color.white;
            _header.hover.textColor = new Color(1f, 0.92f, 0.6f);
            _sectionHeader = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _sectionHeader.normal.textColor = new Color(0.85f, 0.9f, 1f);
            _dim = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            _dim.normal.textColor = new Color(0.72f, 0.72f, 0.78f);
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _small.normal.textColor = new Color(0.72f, 0.72f, 0.78f);
            _error = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _error.normal.textColor = new Color(1f, 0.45f, 0.4f);
        }
    }
}
