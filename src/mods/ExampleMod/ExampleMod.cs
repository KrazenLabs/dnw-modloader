using DnWModLoader;
using DnWModLoader.Config;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ExampleMod
{
    // A small mod that shows the mod loader API: config entries, callbacks, logging and Harmony patches
    public sealed class ExampleMod : Mod
    {
        public static ExampleMod Instance { get; private set; }

        private ConfigEntry<bool> _showHud;
        private ConfigEntry<string> _greeting;
        private ConfigEntry<float> _hudOpacity;
        private ConfigEntry<int> _hudFontSize;
        private ConfigEntry<HudCorner> _hudCorner;
        private ConfigEntry<bool> _tagVersionLabel;
        private ConfigEntry<bool> _logMenus;
        private GUIStyle _hudStyle;

        public enum HudCorner { BottomLeft, BottomRight, TopLeft, TopRight }

        public bool TagVersionLabel { get { return _tagVersionLabel.Value; } }
        public bool LogMenus { get { return _logMenus.Value; } }

        public override void OnInitialize()
        {
            Instance = this;

            Config.DescribeSection("HUD", "Status line", "A line of text drawn by this mod's OnGUI callback.");
            _showHud = Config.Bind("HUD", "ShowHud", true, "Draw the status line.");
            _greeting = Config.Bind("HUD", "Greeting", "Hello from ExampleMod!", "Text shown in the status line.");
            _hudOpacity = Config.Bind("HUD", "Opacity", 0.9f, "Opacity of the status line.", ConfigMeta.Range(0, 1, 0.05));
            _hudFontSize = Config.Bind("HUD", "FontSize", 13, "Font size of the status line.", ConfigMeta.Range(9, 30, 1));
            _hudCorner = Config.Bind("HUD", "Corner", HudCorner.BottomLeft, "Where the status line is drawn.");
            Config.DescribeSection("Patches", "Harmony patches", "Toggles for the example patches.");
            _tagVersionLabel = Config.Bind("Patches", "TagVersionLabel", true, "Prefix the version number in the main menu with 'modded |'.", new ConfigMeta { RequiresRestart = true });
            _logMenus = Config.Bind("Patches", "LogMenuRegistrations", true, "Log every menu the game registers.");

            Logger.Info("Initialized. Loader " + ModLoader.Version + ", game folder " + ModLoader.GameDirectory);
        }

        public override void OnAllModsInitialized()
        {
            Logger.Info(ModLoader.Mods.Count + " mod(s) known to the loader.");
        }

        public override void OnGameStarted()
        {
            Logger.Info("Game started; active scene: " + SceneManager.GetActiveScene().name);
        }

        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.Info("Scene loaded: " + scene.name + " (" + mode + ")");
        }

        public override void OnGUI()
        {
            if (!_showHud.Value) return;
            string dragon;
            try { dragon = WalkNWashSceneState.GetDragonState().ToString(); }
            catch { dragon = "n/a"; }
            if (_hudStyle == null) _hudStyle = new GUIStyle(GUI.skin.label);
            _hudStyle.fontSize = _hudFontSize.Value;
            _hudStyle.normal.textColor = new Color(1f, 1f, 1f, _hudOpacity.Value);
            bool right = _hudCorner.Value == HudCorner.BottomRight || _hudCorner.Value == HudCorner.TopRight;
            bool top = _hudCorner.Value == HudCorner.TopLeft || _hudCorner.Value == HudCorner.TopRight;
            _hudStyle.alignment = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            float height = _hudFontSize.Value + 10f;
            GUI.Label(new Rect(12, top ? 30 : Screen.height - height - 4, Screen.width - 24, height),
                _greeting.Value + "  |  dragon: " + dragon + "  |  scene: " + SceneManager.GetActiveScene().name, _hudStyle);
        }
    }

    // Harmony patch example: Simply logs all game menus
    [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.RegisterMenu))]
    internal static class MenuManager_RegisterMenu_Patch
    {
        private static void Postfix(Menu menu)
        {
            var mod = ExampleMod.Instance;
            if (mod != null && mod.LogMenus) mod.Logger.Info("Menu registered: " + (menu != null ? menu.name : "null"));
        }
    }

    // Version label in the main menu
    [HarmonyPatch(typeof(VersionNumber), "Start")]
    internal static class VersionNumber_Start_Patch
    {
        private static void Postfix(VersionNumber __instance)
        {
            var mod = ExampleMod.Instance;
            if (mod == null || !mod.TagVersionLabel) return;
            var text = __instance.GetComponent<TMPro.TMP_Text>();
            if (text == null || text.text.Contains("modded")) return;

            text.alignment = TMPro.TextAlignmentOptions.BottomRight;
            text.overflowMode = TMPro.TextOverflowModes.Overflow;
            text.color = new Color(1f, 0.85f, 0.4f);
            text.text = "modded | " + text.text;
            mod.Logger.Info("Version label tagged: \"" + text.text + "\"");
        }
    }

    // Show current dragon state
    [HarmonyPatch(typeof(WalkNWashSceneState), nameof(WalkNWashSceneState.SetDragonState), new[] { typeof(WalkNWashSceneState.DragonState) })]
    internal static class WalkNWashSceneState_SetDragonState_Patch
    {
        private static void Prefix(WalkNWashSceneState.DragonState state)
        {
            ExampleMod.Instance?.Logger.Debug("Dragon state -> " + state);
        }
    }
}
