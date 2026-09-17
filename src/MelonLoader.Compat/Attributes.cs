using System;
using System.Drawing;
using Semver;

namespace MelonLoader
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonInfoAttribute : Attribute
    {
        public MelonInfoAttribute(Type type, string name, string version, string author = null, string downloadLink = null)
        {
            SystemType = type;
            Name = name ?? "UNKNOWN";
            Version = string.IsNullOrEmpty(version) ? "1.0.0" : version;
            Author = author;
            DownloadLink = downloadLink;
            SemVersion parsed;
            SemanticVersion = SemVersion.TryParse(Version, out parsed) ? parsed : null;
        }

        public MelonInfoAttribute(Type type, string name, int versionMajor, int versionMinor, int versionRevision, string downloadLink = null)
            : this(type, name, versionMajor + "." + versionMinor + "." + versionRevision, null, downloadLink) { }

        public MelonInfoAttribute(Type type, string name, int versionMajor, int versionMinor, int versionRevision, string versionIdentifier, string author, string downloadLink = null)
            : this(type, name, versionMajor + "." + versionMinor + "." + versionRevision + (string.IsNullOrEmpty(versionIdentifier) ? "" : "-" + versionIdentifier), author, downloadLink) { }

        public Type SystemType { get; private set; }
        public string Name { get; private set; }
        public string Version { get; private set; }
        public SemVersion SemanticVersion { get; private set; }
        public string Author { get; private set; }
        public string DownloadLink { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public class MelonGameAttribute : Attribute
    {
        public MelonGameAttribute(string developer = null, string name = null)
        {
            Developer = developer;
            Name = name;
            Universal = string.IsNullOrEmpty(developer) || string.IsNullOrEmpty(name);
        }

        public string Developer { get; private set; }
        public string Name { get; private set; }
        public bool Universal { get; private set; }

        public bool IsCompatible(string developer, string gameName)
        {
            if (Universal || string.IsNullOrEmpty(developer) || string.IsNullOrEmpty(gameName)) return true;
            return Developer.Equals(developer, StringComparison.OrdinalIgnoreCase)
                   && Name.Equals(gameName, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCompatible(MelonGameAttribute att)
        {
            return att == null || Universal || att.Universal || IsCompatible(att.Developer, att.Name);
        }

        public bool IsCompatibleBecauseUniversal(MelonGameAttribute att)
        {
            return att != null && (Universal || att.Universal);
        }
    }

    // MelonLoader's older mod- and plugin-specific spellings; some mods still use them
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonModInfoAttribute : MelonInfoAttribute
    {
        public MelonModInfoAttribute(Type type, string name, string version, string author = null, string downloadLink = null)
            : base(type, name, version, author, downloadLink) { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonPluginInfoAttribute : MelonInfoAttribute
    {
        public MelonPluginInfoAttribute(Type type, string name, string version, string author = null, string downloadLink = null)
            : base(type, name, version, author, downloadLink) { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public class MelonModGameAttribute : MelonGameAttribute
    {
        public MelonModGameAttribute(string developer = null, string name = null) : base(developer, name) { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public class MelonPluginGameAttribute : MelonGameAttribute
    {
        public MelonPluginGameAttribute(string developer = null, string name = null) : base(developer, name) { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonPriorityAttribute : Attribute
    {
        public MelonPriorityAttribute(int priority = 0) { Priority = priority; }
        public int Priority { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonColorAttribute : Attribute
    {
        public MelonColorAttribute() { Color = ConsoleColor.Gray; DrawingColor = System.Drawing.Color.LightGray; }
        public MelonColorAttribute(ConsoleColor color) { Color = color; DrawingColor = System.Drawing.Color.LightGray; }

        public MelonColorAttribute(int alpha, int red, int green, int blue)
        {
            Color = ConsoleColor.Gray;
            DrawingColor = System.Drawing.Color.FromArgb(alpha, red, green, blue);
        }

        public ConsoleColor Color { get; private set; }
        public Color DrawingColor { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonAuthorColorAttribute : Attribute
    {
        public MelonAuthorColorAttribute() { Color = ConsoleColor.Gray; DrawingColor = System.Drawing.Color.LightGray; }
        public MelonAuthorColorAttribute(ConsoleColor color) { Color = color; DrawingColor = System.Drawing.Color.LightGray; }

        public MelonAuthorColorAttribute(int alpha, int red, int green, int blue)
        {
            Color = ConsoleColor.Gray;
            DrawingColor = System.Drawing.Color.FromArgb(alpha, red, green, blue);
        }

        public ConsoleColor Color { get; private set; }
        public Color DrawingColor { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonIDAttribute : Attribute
    {
        public MelonIDAttribute(string id = null) { ID = id; }
        public MelonIDAttribute(int id) { ID = id.ToString(); }
        public string ID { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public class MelonProcessAttribute : Attribute
    {
        public MelonProcessAttribute(string exe_name = null)
        {
            EXE_Name = exe_name;
            Universal = string.IsNullOrEmpty(exe_name) || exe_name.Equals("UNIVERSAL", StringComparison.OrdinalIgnoreCase);
        }

        public string EXE_Name { get; private set; }
        public bool Universal { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public class MelonGameVersionAttribute : Attribute
    {
        public MelonGameVersionAttribute(string version = null)
        {
            Version = version;
            Universal = string.IsNullOrEmpty(version);
        }

        public string Version { get; private set; }
        public bool Universal { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonPlatformAttribute : Attribute
    {
        public enum CompatiblePlatforms { UNIVERSAL, WINDOWS_X86, WINDOWS_X64, ANDROID, LINUX, MAC }

        public MelonPlatformAttribute(params CompatiblePlatforms[] platforms) { Platforms = platforms; }
        public CompatiblePlatforms[] Platforms { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonPlatformDomainAttribute : Attribute
    {
        public enum CompatibleDomains { UNIVERSAL, MONO, IL2CPP }

        public MelonPlatformDomainAttribute(CompatibleDomains domain = CompatibleDomains.UNIVERSAL) { Domain = domain; }
        public CompatibleDomains Domain { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonAdditionalCreditsAttribute : Attribute
    {
        public MelonAdditionalCreditsAttribute(string credits = null) { Credits = credits; }
        public string Credits { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonAdditionalDependenciesAttribute : Attribute
    {
        public MelonAdditionalDependenciesAttribute(params string[] assemblyNames) { AssemblyNames = assemblyNames; }
        public string[] AssemblyNames { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonOptionalDependenciesAttribute : Attribute
    {
        public MelonOptionalDependenciesAttribute(params string[] assemblyNames) { AssemblyNames = assemblyNames; }
        public string[] AssemblyNames { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class MelonIncompatibleAssembliesAttribute : Attribute
    {
        public MelonIncompatibleAssembliesAttribute(params string[] assemblyNames) { AssemblyNames = assemblyNames; }
        public string[] AssemblyNames { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class VerifyLoaderVersionAttribute : Attribute
    {
        public VerifyLoaderVersionAttribute(string version, bool is_minimum = false)
        {
            SemVersion parsed;
            SemVer = SemVersion.TryParse(version, out parsed) ? parsed : null;
            IsMinimum = is_minimum;
        }

        public VerifyLoaderVersionAttribute(SemVersion semver, bool is_minimum = false) { SemVer = semver; IsMinimum = is_minimum; }
        public VerifyLoaderVersionAttribute(int major, int minor, int patch) : this(new SemVersion(major, minor, patch), false) { }
        public VerifyLoaderVersionAttribute(int major, int minor, int patch, bool is_minimum) : this(new SemVersion(major, minor, patch), is_minimum) { }

        public VerifyLoaderVersionAttribute(int major, int minor, int patch, string prerelease, bool is_minimum = false)
            : this(new SemVersion(major, minor, patch, prerelease), is_minimum) { }

        public SemVersion SemVer { get; private set; }
        public bool IsMinimum { get; private set; }
        public int Major { get { return SemVer != null ? SemVer.Major : 0; } }
        public int Minor { get { return SemVer != null ? SemVer.Minor : 0; } }
        public int Patch { get { return SemVer != null ? SemVer.Patch : 0; } }
        public string Prerelease { get { return SemVer != null ? SemVer.Prerelease : null; } }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public class VerifyLoaderBuildAttribute : Attribute
    {
        public VerifyLoaderBuildAttribute(string hashcode = null) { HashCode = hashcode; }
        public string HashCode { get; private set; }
    }

    // Stops the host from calling HarmonyInstance.PatchAll for this melon.
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class HarmonyDontPatchAllAttribute : Attribute { }

    // Marks code that must never be patched. MelonLoader enforces this with a patch guard; inert here.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    public class PatchShield : Attribute { }

    // Il2Cpp-only. Drag'n Wash is a Mono game, so these do nothing.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class RegisterTypeInIl2Cpp : Attribute
    {
        public RegisterTypeInIl2Cpp() { }
        public RegisterTypeInIl2Cpp(bool logSuccess) { LogSuccess = logSuccess; }
        public bool LogSuccess { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class RegisterTypeInIl2CppWithInterfaces : Attribute
    {
        public RegisterTypeInIl2CppWithInterfaces(params Type[] interfaces) { Interfaces = interfaces; }

        public RegisterTypeInIl2CppWithInterfaces(bool logSuccess, params Type[] interfaces)
        {
            LogSuccess = logSuccess;
            Interfaces = interfaces;
        }

        public bool LogSuccess { get; private set; }
        public Type[] Interfaces { get; private set; }
    }
}
