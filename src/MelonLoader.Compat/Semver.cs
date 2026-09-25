using System;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

namespace Semver
{
    [Serializable]
    public sealed class SemVersion : IComparable<SemVersion>, IComparable, IEquatable<SemVersion>, ISerializable
    {
        public SemVersion(int major, int minor = 0, int patch = 0, string prerelease = "", string build = "")
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease ?? "";
            Build = build ?? "";
        }

        public SemVersion(Version version)
        {
            if (version == null) throw new ArgumentNullException("version");
            Major = version.Major;
            Minor = version.Minor;
            Patch = version.Build > 0 ? version.Build : 0;
            Prerelease = "";
            Build = version.Revision > 0 ? version.Revision.ToString(CultureInfo.InvariantCulture) : "";
        }

        private SemVersion(SerializationInfo info, StreamingContext context)
        {
            if (info == null) throw new ArgumentNullException("info");
            var parsed = Parse(info.GetString("SemVersion"));
            Major = parsed.Major;
            Minor = parsed.Minor;
            Patch = parsed.Patch;
            Prerelease = parsed.Prerelease;
            Build = parsed.Build;
        }

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        public string Prerelease { get; private set; }
        public string Build { get; private set; }

        public static SemVersion Parse(string version, bool strict = false)
        {
            SemVersion result;
            if (!TryParse(version, out result, strict)) throw new ArgumentException("Invalid version: " + version, "version");
            return result;
        }

        public static bool TryParse(string version, out SemVersion semver, bool strict = false)
        {
            semver = null;
            if (string.IsNullOrEmpty(version)) return false;

            string rest = version.Trim();
            string build = "";
            string prerelease = "";

            int plus = rest.IndexOf('+');
            if (plus >= 0) { build = rest.Substring(plus + 1); rest = rest.Substring(0, plus); }
            int dash = rest.IndexOf('-');
            if (dash >= 0) { prerelease = rest.Substring(dash + 1); rest = rest.Substring(0, dash); }

            var parts = rest.Split('.');
            if (parts.Length == 0 || parts.Length > 3) return false;
            if (strict && parts.Length != 3) return false;

            int major, minor = 0, patch = 0;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major)) return false;
            if (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor)) return false;
            if (parts.Length > 2 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;

            semver = new SemVersion(major, minor, patch, prerelease, build);
            return true;
        }

        public static int Compare(SemVersion left, SemVersion right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (ReferenceEquals(left, null)) return -1;
            if (ReferenceEquals(right, null)) return 1;
            return left.CompareTo(right);
        }

        public int CompareTo(SemVersion other)
        {
            return CompareByPrecedence(other);
        }

        public int CompareByPrecedence(SemVersion other)
        {
            if (ReferenceEquals(other, null)) return 1;
            int r = Major.CompareTo(other.Major); if (r != 0) return r;
            r = Minor.CompareTo(other.Minor); if (r != 0) return r;
            r = Patch.CompareTo(other.Patch); if (r != 0) return r;
            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        public bool PrecedenceMatches(SemVersion other)
        {
            return CompareByPrecedence(other) == 0;
        }

        public SemVersion Change(int? major = null, int? minor = null, int? patch = null, string prerelease = null, string build = null)
        {
            return new SemVersion(major ?? Major, minor ?? Minor, patch ?? Patch, prerelease ?? Prerelease, build ?? Build);
        }

        public int CompareTo(object obj)
        {
            return CompareTo(obj as SemVersion);
        }

        private static int ComparePrerelease(string a, string b)
        {
            bool emptyA = string.IsNullOrEmpty(a), emptyB = string.IsNullOrEmpty(b);
            if (emptyA && emptyB) return 0;
            if (emptyA) return 1;
            if (emptyB) return -1;

            var partsA = a.Split('.');
            var partsB = b.Split('.');
            int count = Math.Min(partsA.Length, partsB.Length);
            for (int i = 0; i < count; i++)
            {
                int numA, numB;
                bool isNumA = int.TryParse(partsA[i], NumberStyles.None, CultureInfo.InvariantCulture, out numA);
                bool isNumB = int.TryParse(partsB[i], NumberStyles.None, CultureInfo.InvariantCulture, out numB);
                int r;
                if (isNumA && isNumB) r = numA.CompareTo(numB);
                else if (isNumA) r = -1;
                else if (isNumB) r = 1;
                else r = string.CompareOrdinal(partsA[i], partsB[i]);
                if (r != 0) return r;
            }
            return partsA.Length.CompareTo(partsB.Length);
        }

        public bool Equals(SemVersion other)
        {
            return !ReferenceEquals(other, null)
                   && Major == other.Major && Minor == other.Minor && Patch == other.Patch
                   && string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal)
                   && string.Equals(Build, other.Build, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) { return Equals(obj as SemVersion); }

        public static bool Equals(SemVersion versionA, SemVersion versionB)
        {
            if (ReferenceEquals(versionA, versionB)) return true;
            if (ReferenceEquals(versionA, null) || ReferenceEquals(versionB, null)) return false;
            return versionA.Equals(versionB);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Major;
                hash = (hash * 397) ^ Minor;
                hash = (hash * 397) ^ Patch;
                hash = (hash * 397) ^ (Prerelease != null ? Prerelease.GetHashCode() : 0);
                hash = (hash * 397) ^ (Build != null ? Build.GetHashCode() : 0);
                return hash;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Major).Append('.').Append(Minor).Append('.').Append(Patch);
            if (!string.IsNullOrEmpty(Prerelease)) sb.Append('-').Append(Prerelease);
            if (!string.IsNullOrEmpty(Build)) sb.Append('+').Append(Build);
            return sb.ToString();
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null) throw new ArgumentNullException("info");
            info.AddValue("SemVersion", ToString());
        }

        public static implicit operator SemVersion(Version version) { return new SemVersion(version); }
        public static implicit operator SemVersion(string version) { return Parse(version); }

        public static bool operator ==(SemVersion left, SemVersion right) { return Equals(left, right); }
        public static bool operator !=(SemVersion left, SemVersion right) { return !Equals(left, right); }
        public static bool operator <(SemVersion left, SemVersion right) { return Compare(left, right) < 0; }
        public static bool operator >(SemVersion left, SemVersion right) { return Compare(left, right) > 0; }
        public static bool operator <=(SemVersion left, SemVersion right) { return Equals(left, right) || Compare(left, right) < 0; }
        public static bool operator >=(SemVersion left, SemVersion right) { return Equals(left, right) || Compare(left, right) > 0; }
    }
}
