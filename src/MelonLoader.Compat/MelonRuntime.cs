using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MelonLoader.Utils;
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
        public static MelonGameAttribute CurrentGameAttribute { get { return new MelonGameAttribute(GameDeveloper, GameName); } }

        public static PlatformID GetPlatform { get { return Environment.OSVersion.Platform; } }
        public static MelonPlatformAttribute.CompatiblePlatforms CurrentPlatform
        {
            get { return Environment.Is64BitProcess ? MelonPlatformAttribute.CompatiblePlatforms.WINDOWS_X64 : MelonPlatformAttribute.CompatiblePlatforms.WINDOWS_X86; }
        }
        public static MelonPlatformDomainAttribute.CompatibleDomains CurrentDomain { get { return MelonPlatformDomainAttribute.CompatibleDomains.MONO; } }

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
        public static string GetApplicationPath() { return MelonEnvironment.GameExecutablePath; }

        public static int Clamp(int value, int min, int max) { return value < min ? min : value > max ? max : value; }
        public static float Clamp(float value, float min, float max) { return value < min ? min : value > max ? max : value; }
        public static double Clamp(double value, double min, double max) { return value < min ? min : value > max ? max : value; }

        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }

        public static string MakePlural(int count, string word)
        {
            return count == 1 ? word : word + "s";
        }

        public static string MakePlural(this string str, int amount)
        {
            return amount == 1 ? str : str + "s";
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

        public static IEnumerable<Type> GetValidTypes(this Assembly asm)
        {
            return GetValidTypes(asm, null);
        }

        public static IEnumerable<Type> GetValidTypes(this Assembly asm, LemonFunc<Type, bool> predicate)
        {
            Type[] types;
            if (asm == null) return Enumerable.Empty<Type>();
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types ?? new Type[0]; }
            catch { return Enumerable.Empty<Type>(); }
            return types.Where(t => t != null && (predicate == null || predicate(t))).ToArray();
        }

        public static Type GetValidType(this Assembly asm, string typeName)
        {
            return GetValidType(asm, typeName, null);
        }

        public static Type GetValidType(this Assembly asm, string typeName, LemonFunc<Type, bool> predicate)
        {
            Type type;
            try { type = asm != null ? asm.GetType(typeName) : null; }
            catch { type = null; }
            return type != null && (predicate == null || predicate(type)) ? type : null;
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
