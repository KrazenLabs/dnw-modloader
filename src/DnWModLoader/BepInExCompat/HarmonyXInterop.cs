using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DnWModLoader.BepInExCompat
{
    internal static class HarmonyXInterop
    {
        private const string HarmonyAssembly = "0Harmony";
        private const string CompatNamespace = "DnWModLoader.Compat";
        private const string CompatTypesNamespace = "DnWModLoader.Compat.HarmonyX";

        private static readonly Dictionary<string, string> Methods = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Reflection.MethodInfo HarmonyLib.Harmony::Patch(System.Reflection.MethodBase,HarmonyLib.HarmonyMethod,HarmonyLib.HarmonyMethod,HarmonyLib.HarmonyMethod,HarmonyLib.HarmonyMethod,HarmonyLib.HarmonyMethod)"] = "Patch",
            ["System.Void HarmonyLib.Harmony::PatchAll(System.Type)"] = "PatchAll",
            ["HarmonyLib.Harmony HarmonyLib.Harmony::CreateAndPatchAll(System.Type,System.String)"] = "CreateAndPatchAll",
            ["HarmonyLib.Harmony HarmonyLib.Harmony::CreateAndPatchAll(System.Reflection.Assembly,System.String)"] = "CreateAndPatchAll",
            ["HarmonyLib.PatchClassProcessor HarmonyLib.Harmony::CreateClassProcessor(System.Type,System.Boolean)"] = "CreateClassProcessor",
            ["System.Void HarmonyLib.PatchClassProcessor::.ctor(HarmonyLib.Harmony,System.Type,System.Boolean)"] = "NewPatchClassProcessor",
            ["System.Void HarmonyLib.Harmony::UnpatchSelf()"] = "UnpatchSelf",
            ["System.Void HarmonyLib.Harmony::UnpatchID(System.String)"] = "UnpatchID",
            ["System.Void HarmonyLib.Harmony::UnpatchAll()"] = "UnpatchAll",
            ["System.Reflection.MethodInfo HarmonyLib.Harmony::ReversePatch(System.Reflection.MethodBase,HarmonyLib.HarmonyMethod,System.Reflection.MethodInfo,System.Reflection.MethodInfo)"] = "ReversePatch",
            ["HarmonyLib.CodeMatcher HarmonyLib.CodeMatcher::MatchForward(System.Boolean,HarmonyLib.CodeMatch[])"] = "MatchForward",
            ["HarmonyLib.CodeMatcher HarmonyLib.CodeMatcher::MatchBack(System.Boolean,HarmonyLib.CodeMatch[])"] = "MatchBack",
            ["HarmonyLib.CodeMatcher HarmonyLib.CodeMatcher::SearchBack(System.Func`2<HarmonyLib.CodeInstruction,System.Boolean>)"] = "SearchBack",
            ["HarmonyLib.CodeMatch HarmonyLib.CodeMatch::op_Implicit(System.Reflection.Emit.OpCode)"] = "CodeMatchFromOpCode",
            ["HarmonyLib.CodeMatch HarmonyLib.CodeMatch::op_Implicit(HarmonyLib.CodeInstruction)"] = "CodeMatchFromInstruction",
            ["HarmonyLib.CodeInstruction HarmonyLib.Transpilers::EmitDelegate(!!0)"] = "EmitDelegate",
        };

        private static readonly HashSet<string> PatchAttributeConstructors = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Void HarmonyLib.HarmonyPatch::.ctor(System.String,System.String)",
            "System.Void HarmonyLib.HarmonyPatch::.ctor(System.String,System.String,HarmonyLib.MethodType,System.Type[],HarmonyLib.ArgumentType[])",
        };

        // HarmonyX fields
        private static readonly Dictionary<string, string> FieldsToProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Collections.Generic.List`1<System.Reflection.Emit.OpCode> HarmonyLib.CodeMatch::opcodes"] = "opcodes",
        };

        // HarmonyX-only types with stand-ins
        private static readonly HashSet<string> Types = new HashSet<string>(StringComparer.Ordinal)
        {
            "HarmonyLib.HarmonyWrapSafe",
            "HarmonyLib.HarmonyILManipulator",
            "HarmonyLib.HarmonyEmitIL",
            "HarmonyLib.HarmonyGlobalSettings",
            "HarmonyLib.MemberNotFoundException",
            "HarmonyLib.InvalidHarmonyPatchArgumentException",
        };

        internal sealed class Result
        {
            // For logging
            public readonly List<string> Redirected = new List<string>();
            public readonly List<string> Unsupported = new List<string>();

            public bool Changed { get { return Redirected.Count > 0; } }
        }

        public static Result Apply(ModuleDefinition module)
        {
            var result = new Result();
            if (!module.AssemblyReferences.Any(r => r.Name == HarmonyAssembly)) return result;

            var methodKeys = new HashSet<string>(StringComparer.Ordinal);
            var fieldKeys = new HashSet<string>(StringComparer.Ordinal);
            var typeRefs = new List<TypeReference>();

            foreach (var type in module.GetTypeReferences())
            {
                if (!InHarmony(type) || Resolves(type)) continue;
                if (Types.Contains(type.FullName)) typeRefs.Add(type);
                else AddOnce(result.Unsupported, type.FullName);
            }

            foreach (var member in module.GetMemberReferences())
            {
                if (!InHarmony(member.DeclaringType) || Resolves(member)) continue;
                string key = member.FullName;
                if (member is MethodReference && (Methods.ContainsKey(key) || PatchAttributeConstructors.Contains(key))) methodKeys.Add(key);
                else if (member is FieldReference && FieldsToProperties.ContainsKey(key)) fieldKeys.Add(key);
                else if (!Types.Contains(member.DeclaringType.GetElementType().FullName)) AddOnce(result.Unsupported, key);
            }

            if (methodKeys.Count == 0 && fieldKeys.Count == 0 && typeRefs.Count == 0) return result;

            var loaderScope = LoaderScope(module);
            var compatType = new TypeReference(CompatNamespace, "HarmonyXCompat", module, loaderScope);
            var patchAttributeType = new TypeReference(CompatTypesNamespace, "HarmonyPatch", module, loaderScope);

            if (methodKeys.Count > 0 || fieldKeys.Count > 0)
            {
                foreach (var type in module.GetTypes())
                    foreach (var method in type.Methods)
                        if (method.HasBody) RewriteBody(method, methodKeys, fieldKeys, compatType, result);

                foreach (var provider in AttributeProviders(module))
                {
                    if (!provider.HasCustomAttributes) continue;
                    foreach (var attribute in provider.CustomAttributes)
                    {
                        string key = attribute.Constructor.FullName;
                        if (!methodKeys.Contains(key) || !PatchAttributeConstructors.Contains(key)) continue;

                        var arguments = attribute.ConstructorArguments.ToList();
                        var constructor = new MethodReference(".ctor", module.TypeSystem.Void, patchAttributeType) { HasThis = true };
                        foreach (var parameter in attribute.Constructor.Parameters) constructor.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
                        attribute.Constructor = constructor;
                        attribute.ConstructorArguments.Clear();
                        foreach (var argument in arguments) attribute.ConstructorArguments.Add(argument);
                        AddOnce(result.Redirected, key);
                    }
                }
            }

            // Type ref renaming
            foreach (var type in typeRefs)
            {
                AddOnce(result.Redirected, type.FullName);
                type.Namespace = CompatTypesNamespace;
                type.Scope = loaderScope;
            }
            return result;
        }

        private static void RewriteBody(MethodDefinition method, HashSet<string> methodKeys, HashSet<string> fieldKeys, TypeReference compatType, Result result)
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is MethodReference called)
                {
                    var element = called is GenericInstanceMethod generic ? generic.ElementMethod : called;
                    string key = element.FullName;
                    if (!methodKeys.Contains(key) || !Methods.TryGetValue(key, out string standInName)) continue;

                    var code = instruction.OpCode.Code;
                    if (code != Code.Call && code != Code.Callvirt && code != Code.Newobj)
                    {
                        AddOnce(result.Unsupported, key + " (as a delegate)");
                        continue;
                    }
                    var standIn = StandIn(element, standInName, compatType);
                    if (called is GenericInstanceMethod instance)
                    {
                        var genericStandIn = new GenericInstanceMethod(standIn);
                        foreach (var argument in instance.GenericArguments) genericStandIn.GenericArguments.Add(argument);
                        instruction.Operand = genericStandIn;
                    }
                    else
                    {
                        instruction.Operand = standIn;
                    }
                    instruction.OpCode = OpCodes.Call;
                    AddOnce(result.Redirected, key);
                }
                else if (instruction.Operand is FieldReference field && fieldKeys.Contains(field.FullName))
                {
                    string property = FieldsToProperties[field.FullName];
                    var code = instruction.OpCode.Code;
                    if (code == Code.Ldfld)
                    {
                        instruction.OpCode = OpCodes.Callvirt;
                        instruction.Operand = new MethodReference("get_" + property, field.FieldType, field.DeclaringType) { HasThis = true };
                    }
                    else if (code == Code.Stfld)
                    {
                        var setter = new MethodReference("set_" + property, method.Module.TypeSystem.Void, field.DeclaringType) { HasThis = true };
                        setter.Parameters.Add(new ParameterDefinition(field.FieldType));
                        instruction.OpCode = OpCodes.Callvirt;
                        instruction.Operand = setter;
                    }
                    else
                    {
                        AddOnce(result.Unsupported, field.FullName + " (by address)");
                        continue;
                    }
                    AddOnce(result.Redirected, field.FullName);
                }
            }
        }

        // Static handling
        private static MethodReference StandIn(MethodReference original, string name, TypeReference compatType)
        {
            var standIn = new MethodReference(name, original.ReturnType, compatType) { HasThis = false };
            foreach (var parameter in original.GenericParameters) standIn.GenericParameters.Add(new GenericParameter(parameter.Name, standIn));

            bool constructor = original.Name == ".ctor";
            if (original.HasThis && !constructor) standIn.Parameters.Add(new ParameterDefinition(original.DeclaringType));
            foreach (var parameter in original.Parameters) standIn.Parameters.Add(new ParameterDefinition(Remap(parameter.ParameterType, standIn)));
            standIn.ReturnType = constructor ? original.DeclaringType : Remap(original.ReturnType, standIn);
            return standIn;
        }

        // Points method generic parameters (!!0) at the stand-in's own
        private static TypeReference Remap(TypeReference type, MethodReference owner)
        {
            switch (type)
            {
                case GenericParameter parameter when parameter.Type == GenericParameterType.Method:
                    return owner.GenericParameters[parameter.Position];
                case GenericInstanceType instance:
                    var copy = new GenericInstanceType(instance.ElementType);
                    foreach (var argument in instance.GenericArguments) copy.GenericArguments.Add(Remap(argument, owner));
                    return copy;
                case ArrayType array:
                    return new ArrayType(Remap(array.ElementType, owner), array.Rank);
                case ByReferenceType byReference:
                    return new ByReferenceType(Remap(byReference.ElementType, owner));
                default:
                    return type;
            }
        }

        private static AssemblyNameReference LoaderScope(ModuleDefinition module)
        {
            var existing = module.AssemblyReferences.FirstOrDefault(r => r.Name == "DnWModLoader");
            if (existing != null) return existing;
            var reference = new AssemblyNameReference("DnWModLoader", typeof(ModLoader).Assembly.GetName().Version);
            module.AssemblyReferences.Add(reference);
            return reference;
        }

        private static bool InHarmony(TypeReference type)
        {
            return type != null && type.Scope != null && type.Scope.MetadataScopeType == MetadataScopeType.AssemblyNameReference && type.Scope.Name == HarmonyAssembly;
        }

        private static bool Resolves(MemberReference member)
        {
            try
            {
                switch (member)
                {
                    case MethodReference method: return method.Resolve() != null;
                    case FieldReference field: return field.Resolve() != null;
                    case TypeReference type: return type.Resolve() != null;
                    default: return true;
                }
            }
            catch (AssemblyResolutionException)
            {
                return false;
            }
        }

        private static void AddOnce(List<string> list, string item)
        {
            if (!list.Contains(item)) list.Add(item);
        }

        private static IEnumerable<ICustomAttributeProvider> AttributeProviders(ModuleDefinition module)
        {
            yield return module.Assembly;
            yield return module;
            foreach (var type in module.GetTypes())
            {
                yield return type;
                foreach (var parameter in type.GenericParameters) yield return parameter;
                foreach (var field in type.Fields) yield return field;
                foreach (var property in type.Properties) yield return property;
                foreach (var @event in type.Events) yield return @event;
                foreach (var method in type.Methods)
                {
                    yield return method;
                    yield return method.MethodReturnType;
                    foreach (var parameter in method.Parameters) yield return parameter;
                    foreach (var parameter in method.GenericParameters) yield return parameter;
                }
            }
        }
    }
}
