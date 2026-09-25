using DnWModLoader;
using DnWModLoader.Config;
using HarmonyLib;
using UnityEngine;

namespace MyMod
{
    public sealed class MyMod : Mod
    {
        public static MyMod Instance { get; private set; }

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _strength;

        // Earliest callback: before any scene and before the game's own static initializers
        public override void OnInitialize()
        {
            Instance = this;
            Config.DescribeSection("General", "General", "Main options of MyMod.");
            _enabled = Config.Bind("General", "Enabled", true, "Master switch for this mod.");
            _strength = Config.Bind("General", "Strength", 1f, "How strong the effect is.", ConfigMeta.Range(0, 2, 0.1));
            _strength.Changed += value => Logger.Info("Strength is now " + value);
            Logger.Info("MyMod initialized (loader " + ModLoader.ParsedVersion + ")");
            // [HarmonyPatch] classes are applied automatically after this method returns
            // Set `public override bool AutoPatch => false;` and call Harmony.PatchAll() yourself for manual control
        }

        public override void OnGameStarted()
        {
            Logger.Info("Game started.");
        }

        public override void OnUpdate()
        {
            if (!_enabled.Value) return;
        }
    }

    // Example patch
    [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.RegisterMenu))]
    internal static class MenuManager_RegisterMenu_Patch
    {
        private static void Postfix(Menu menu)
        {
            MyMod.Instance?.Logger.Debug("Menu registered: " + menu.name);
        }
    }
}
