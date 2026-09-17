using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace MelonLoader
{
    // Loaded melon DLL and the melons found
    public class MelonAssembly
    {
        private static readonly List<MelonAssembly> Loaded = new List<MelonAssembly>();
        private readonly List<MelonBase> _melons = new List<MelonBase>();
        private readonly List<RottenMelon> _rotten = new List<RottenMelon>();

        internal MelonAssembly(Assembly assembly, string location)
        {
            Assembly = assembly;
            Location = location ?? "";
            Hash = ComputeHash(location);
            lock (Loaded) Loaded.Add(this);
        }

        public Assembly Assembly { get; private set; }
        public string Location { get; private set; }
        public string Hash { get; private set; }
        public bool HarmonyDontPatchAll { get; internal set; }

        public ReadOnlyCollection<MelonBase> LoadedMelons { get { lock (_melons) return _melons.ToList().AsReadOnly(); } }
        public ReadOnlyCollection<RottenMelon> RottenMelons { get { lock (_rotten) return _rotten.ToList().AsReadOnly(); } }

        public static ReadOnlyCollection<MelonAssembly> LoadedAssemblies { get { lock (Loaded) return Loaded.ToList().AsReadOnly(); } }

        internal void AddMelon(MelonBase melon) { lock (_melons) _melons.Add(melon); }
        internal void AddRotten(RottenMelon rotten) { lock (_rotten) _rotten.Add(rotten); }

        public void UnregisterMelons(string reason = null, bool silent = false)
        {
            foreach (var melon in LoadedMelons) melon.Unregister(reason, silent);
        }

        public static MelonAssembly GetMelonAssemblyOfMember(MemberInfo member, object obj = null)
        {
            if (member == null) return null;
            var assembly = member.DeclaringType != null ? member.DeclaringType.Assembly : null;
            if (assembly == null) return null;
            lock (Loaded) return Loaded.FirstOrDefault(a => a.Assembly == assembly);
        }

        private static string ComputeHash(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }
    }

    // A melon that could not be loaded
    public class RottenMelon
    {
        public RottenMelon(Type type, string errorMessage, Exception exception = null)
        {
            Type = type;
            ErrorMessage = errorMessage;
            Exception = exception;
        }

        public Type Type { get; private set; }
        public string ErrorMessage { get; private set; }
        public Exception Exception { get; private set; }
    }

    public class ResolvedMelons
    {
        public ResolvedMelons(MelonBase[] melons, RottenMelon[] rotten)
        {
            loadedMelons = melons ?? new MelonBase[0];
            rottenMelons = rotten ?? new RottenMelon[0];
        }

        public readonly MelonBase[] loadedMelons;
        public readonly RottenMelon[] rottenMelons;
    }
}
