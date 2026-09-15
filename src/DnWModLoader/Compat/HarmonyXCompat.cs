using System;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DnWModLoader.Compat
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class HarmonyXCompat
    {
        public static MethodInfo Patch(Harmony harmony, MethodBase original, HarmonyMethod prefix, HarmonyMethod postfix, HarmonyMethod transpiler, HarmonyMethod finalizer, HarmonyMethod ilmanipulator)
        {
            if (ilmanipulator != null) throw IlManipulatorsUnsupported(harmony.Id);
            return harmony.Patch(original, prefix, postfix, transpiler, finalizer);
        }

        public static void PatchAll(Harmony harmony, Type type)
        {
            harmony.CreateClassProcessor(type).Patch();
        }

        public static Harmony CreateAndPatchAll(Type type, string harmonyInstanceId)
        {
            var harmony = new Harmony(harmonyInstanceId ?? "harmony-auto-" + Guid.NewGuid());
            PatchAll(harmony, type);
            return harmony;
        }

        public static Harmony CreateAndPatchAll(Assembly assembly, string harmonyInstanceId)
        {
            var harmony = new Harmony(harmonyInstanceId ?? "harmony-auto-" + Guid.NewGuid());
            harmony.PatchAll(assembly);
            return harmony;
        }

        public static PatchClassProcessor CreateClassProcessor(Harmony harmony, Type type, bool allowUnannotatedType)
        {
            return harmony.CreateClassProcessor(type);
        }

        public static PatchClassProcessor NewPatchClassProcessor(Harmony instance, Type type, bool allowUnannotatedType)
        {
            return new PatchClassProcessor(instance, type);
        }

        public static void UnpatchSelf(Harmony harmony)
        {
            harmony.UnpatchAll(harmony.Id);
        }

        public static void UnpatchID(string harmonyID)
        {
            if (string.IsNullOrEmpty(harmonyID)) throw new ArgumentNullException(nameof(harmonyID), "UnpatchID was called with a null or empty harmonyID.");
            new Harmony(harmonyID).UnpatchAll(harmonyID);
        }

        public static void UnpatchAll()
        {
            ModLoader.Logger.Warning("A plugin called Harmony.UnpatchAll(), which removes every Harmony patch of every mod.");
            new Harmony("dnwmodloader.harmonyx-compat").UnpatchAll();
        }

        public static MethodInfo ReversePatch(MethodBase original, HarmonyMethod standin, MethodInfo transpiler, MethodInfo ilmanipulator)
        {
            if (ilmanipulator != null) throw IlManipulatorsUnsupported(null);
            return Harmony.ReversePatch(original, standin, transpiler);
        }

        public static CodeMatcher MatchForward(CodeMatcher matcher, bool useEnd, CodeMatch[] matches)
        {
            return useEnd ? matcher.MatchEndForward(matches) : matcher.MatchStartForward(matches);
        }

        public static CodeMatcher MatchBack(CodeMatcher matcher, bool useEnd, CodeMatch[] matches)
        {
            return useEnd ? matcher.MatchEndBackwards(matches) : matcher.MatchStartBackwards(matches);
        }

        public static CodeMatcher SearchBack(CodeMatcher matcher, Func<CodeInstruction, bool> predicate)
        {
            return matcher.SearchBackwards(predicate);
        }

        public static CodeMatch CodeMatchFromOpCode(OpCode opcode)
        {
            return new CodeMatch(opcode);
        }

        public static CodeMatch CodeMatchFromInstruction(CodeInstruction instruction)
        {
            return new CodeMatch(instruction);
        }

        public static CodeInstruction EmitDelegate<T>(T action) where T : Delegate
        {
            return CodeInstruction.CallClosure(action);
        }

        private static NotSupportedException IlManipulatorsUnsupported(string harmonyId)
        {
            return new NotSupportedException("HarmonyX IL manipulators are not supported by DnW Mod Loader" + (harmonyId != null ? " (Harmony id " + harmonyId + ")" : ""));
        }
    }
}

namespace DnWModLoader.Compat.HarmonyX
{
    // HarmonyX mapping
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Delegate, AllowMultiple = true)]
    public class HarmonyPatch : HarmonyAttribute
    {
        public HarmonyPatch(string assemblyQualifiedDeclaringType, string methodName)
        {
            info.declaringType = AccessTools.TypeByName(assemblyQualifiedDeclaringType);
            info.methodName = methodName;
        }

        public HarmonyPatch(string assemblyQualifiedDeclaringType, string methodName, MethodType methodType, Type[] argumentTypes, ArgumentType[] argumentVariations)
        {
            var parsed = new HarmonyLib.HarmonyPatch(AccessTools.TypeByName(assemblyQualifiedDeclaringType), methodName, argumentTypes, argumentVariations);
            info = parsed.info;
            info.methodType = methodType;
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class HarmonyWrapSafe : Attribute
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyILManipulator : Attribute
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class HarmonyEmitIL : Attribute
    {
        public HarmonyEmitIL() { }

        public HarmonyEmitIL(string dir) { }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class HarmonyGlobalSettings
    {
        public static bool DisallowLegacyGlobalUnpatchAll { get; set; }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public class MemberNotFoundException : Exception
    {
        public MemberNotFoundException(string message) : base(message) { }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public class InvalidHarmonyPatchArgumentException : Exception
    {
        public MethodBase Original { get; }
        public MethodInfo Patch { get; }

        public InvalidHarmonyPatchArgumentException(string message, MethodBase original, MethodInfo patch) : base(message)
        {
            Original = original;
            Patch = patch;
        }
    }
}
