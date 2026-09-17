using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MelonLoader
{
    // Coroutines run on the loader's own MonoBehaviour
    public class MelonCoroutines
    {
        internal static Func<IEnumerator, object> Starter;
        internal static Action<object> Stopper;

        public static object Start(IEnumerator routine)
        {
            var starter = Starter;
            if (starter == null || routine == null) return null;
            try { return starter(routine); }
            catch (Exception e) { MelonLogger.Error("MelonCoroutines.Start failed: " + e.Message); return null; }
        }

        public static void Stop(object coroutineToken)
        {
            var stopper = Stopper;
            if (stopper == null || coroutineToken == null) return;
            try { stopper(coroutineToken); }
            catch (Exception e) { MelonLogger.Error("MelonCoroutines.Stop failed: " + e.Message); }
        }
    }

    public static class MelonUtils
    {
        internal static string GameDirectoryValue = "";
        internal static string UserDataDirectoryValue = "";
        internal static string UserLibsDirectoryValue = "";
        internal static string MelonLoaderDirectoryValue = "";
        internal static string BaseDirectoryValue = "";

        public static string GameDirectory { get { return GameDirectoryValue; } }
        public static string UserDataDirectory { get { return UserDataDirectoryValue; } }
        public static string UserLibsDirectory { get { return UserLibsDirectoryValue; } }
        public static string MelonLoaderDirectory { get { return MelonLoaderDirectoryValue; } }
        public static string BaseDirectory { get { return BaseDirectoryValue; } }

        public static string GameName { get { return SafeApplication(() => Application.productName); } }
        public static string GameDeveloper { get { return SafeApplication(() => Application.companyName); } }
        public static string GameVersion { get { return SafeApplication(() => Application.version); } }
        public static string CurrentGameAttribute { get { return GameDeveloper + " - " + GameName; } }

        public static bool IsWindows { get { return true; } }
        public static bool IsUnix { get { return false; } }
        public static bool IsMac { get { return false; } }

        public static bool IsGameIl2Cpp() { return false; }
        public static bool IsGame32Bit() { return !Environment.Is64BitProcess; }
        public static bool IsOldMono() { return false; }
        public static bool IsUnderWineOrSteamProton() { return false; }

        public static string GetUnityVersion() { return SafeApplication(() => Application.unityVersion); }
        public static string GetGameDataDirectory() { return SafeApplication(() => Application.dataPath); }
        public static string GetManagedDirectory() { return Path.Combine(GetGameDataDirectory() ?? "", "Managed"); }
        public static string GetApplicationPath() { return GameDirectory; }

        public static AppDomain CurrentDomain { get { return AppDomain.CurrentDomain; } }

        public static int Clamp(int value, int min, int max) { return value < min ? min : value > max ? max : value; }
        public static float Clamp(float value, float min, float max) { return value < min ? min : value > max ? max : value; }
        public static double Clamp(double value, double min, double max) { return value < min ? min : value > max ? max : value; }

        public static string MakePlural(int count, string word)
        {
            return count == 1 ? word : word + "s";
        }

        public static bool IsTypeEqualToName(Type type, string name)
        {
            return type != null && string.Equals(type.Name, name, StringComparison.Ordinal);
        }

        public static bool IsTypeEqualToFullName(Type type, string fullName)
        {
            return type != null && string.Equals(type.FullName, fullName, StringComparison.Ordinal);
        }

        public static bool IsNotImplemented(MethodBase method)
        {
            return method == null;
        }

        public static T PullAttributeFromAssembly<T>(Assembly asm, bool inherit = false) where T : Attribute
        {
            var all = PullAttributesFromAssembly<T>(asm, inherit);
            return all != null && all.Length > 0 ? all[0] : null;
        }

        public static T[] PullAttributesFromAssembly<T>(Assembly asm, bool inherit = false) where T : Attribute
        {
            if (asm == null) return new T[0];
            try { return (T[])Attribute.GetCustomAttributes(asm, typeof(T), inherit); }
            catch { return new T[0]; }
        }

        public static Type[] GetValidTypes(Assembly asm)
        {
            if (asm == null) return new Type[0];
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return Array.FindAll(e.Types, t => t != null); }
            catch { return new Type[0]; }
        }

        public static bool IsManagedDLL(string path)
        {
            try
            {
                AssemblyName.GetAssemblyName(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetPathAncestor(string path, int level)
        {
            for (int i = 0; i < level && !string.IsNullOrEmpty(path); i++) path = Path.GetDirectoryName(path);
            return path;
        }

        private static string SafeApplication(Func<string> get)
        {
            try { return get(); }
            catch { return ""; }
        }
    }

    public static class MelonDebug
    {
        public static bool IsEnabled() { return false; }
        public static void Msg(object obj) { }
        public static void Msg(string txt) { }
        public static void Msg(string txt, params object[] args) { }
        public static void Error(string txt) { MelonLogger.Error(txt); }
    }
}
