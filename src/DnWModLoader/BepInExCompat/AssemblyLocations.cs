using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DnWModLoader.BepInExCompat
{
    // Forwards file locations for rewritten assemblies
    internal static class AssemblyLocations
    {
        private static readonly Dictionary<Assembly, string> Paths = new Dictionary<Assembly, string>();
        private static bool _patchAttempted;

        public static void Register(Assembly assembly, string path)
        {
            lock (Paths) Paths[assembly] = path;
            if (_patchAttempted) return;
            _patchAttempted = true;
            try
            {
                var getter = AccessTools.PropertyGetter(typeof(ModLoader).Assembly.GetType(), nameof(Assembly.Location));
                new Harmony("dnwmodloader.bepinex.assembly-location").Patch(getter, postfix: new HarmonyMethod(typeof(AssemblyLocations), nameof(LocationPostfix)));
            }
            catch (Exception e)
            {
                ModLoader.Logger.Warning("Could not make Assembly.Location report plugin files: " + e.Message);
            }
        }

        private static void LocationPostfix(Assembly __instance, ref string __result)
        {
            if (!string.IsNullOrEmpty(__result)) return;
            lock (Paths)
            {
                if (Paths.TryGetValue(__instance, out string path)) __result = path;
            }
        }
    }
}
