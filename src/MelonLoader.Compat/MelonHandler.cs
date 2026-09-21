using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader.Utils;

namespace MelonLoader
{
    public static class MelonHandler
    {
        [Obsolete("Use MelonEnvironment.ModsDirectory instead. This will be removed in a future update.")]
        public static string ModsDirectory { get { return MelonEnvironment.ModsDirectory; } }

        [Obsolete("Use MelonEnvironment.PluginsDirectory instead. This will be removed in a future update.")]
        public static string PluginsDirectory { get { return MelonEnvironment.PluginsDirectory; } }

        [Obsolete("Use 'MelonMod.RegisteredMelons' instead. This will be removed in a future update.")]
        public static List<MelonMod> Mods { get { return MelonTypeBase<MelonMod>.RegisteredMelons.ToList(); } }

        [Obsolete("Use 'MelonPlugin.RegisteredMelons' instead. This will be removed in a future update.")]
        public static List<MelonPlugin> Plugins { get { return MelonTypeBase<MelonPlugin>.RegisteredMelons.ToList(); } }
    }
}
