using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DnWModLoader
{
    internal static class AssemblyPatcher
    {
        private const string LoaderAssemblyName = "DnWModLoader";
        private const string BootstrapTypeName = "DnWModLoader.Bootstrap";
        private const string BootstrapMethodName = "Init";
        private const string LateMethodName = "AfterRegistration";
        private const int SubsystemRegistration = 4;

        // Phases after SubsystemRegistration, in the order Unity runs them
        private static readonly int[] LatePhases = { 2, 3, 1 };
        private static readonly string[] LatePhaseNames = { "AfterAssembliesLoaded", "BeforeSplashScreen", "BeforeSceneLoad" };

        // Returns patched assembly bytes and descriptions of methods
        public static byte[] Patch(byte[] original, string managedDir, string loaderDllPath, out string targetDescription, out string lateTargetDescription, out string latePhase)
        {
            using (var resolver = CreateResolver(managedDir))
            using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(original), new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadingMode = ReadingMode.Immediate }))
            using (var loader = AssemblyDefinition.ReadAssembly(loaderDllPath, new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
            {
                var module = assembly.MainModule;
                if (module.AssemblyReferences.Any(r => string.Equals(r.Name, LoaderAssemblyName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Assembly-CSharp.dll already references " + LoaderAssemblyName + ".");

                var bootstrap = loader.MainModule.GetType(BootstrapTypeName) ?? throw new InvalidOperationException(loaderDllPath + " does not contain " + BootstrapTypeName);
                var init = bootstrap.Methods.FirstOrDefault(m => m.Name == BootstrapMethodName && m.IsStatic && m.IsPublic && !m.HasParameters)
                           ?? throw new InvalidOperationException(BootstrapTypeName + "." + BootstrapMethodName + "() not found in the loader assembly.");

                var target = FindTarget(module) ?? throw new InvalidOperationException("No static [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] method found in Assembly-CSharp.dll to hook.");
                InsertCall(module, target, init);
                targetDescription = target.DeclaringType.FullName + "." + target.Name;

                // Optional second hook, for BepInEx plugins
                lateTargetDescription = null;
                latePhase = null;
                var late = bootstrap.Methods.FirstOrDefault(m => m.Name == LateMethodName && m.IsStatic && m.IsPublic && !m.HasParameters);
                if (late != null && FindLateTarget(module, out var lateTarget, out latePhase))
                {
                    InsertCall(module, lateTarget, late);
                    lateTargetDescription = lateTarget.DeclaringType.FullName + "." + lateTarget.Name;
                }

                using (var output = new MemoryStream())
                {
                    assembly.Write(output);
                    return output.ToArray();
                }
            }
        }

        private static void InsertCall(ModuleDefinition module, MethodDefinition target, MethodDefinition callee)
        {
            var il = target.Body.GetILProcessor();
            il.InsertBefore(target.Body.Instructions[0], il.Create(OpCodes.Call, module.ImportReference(callee)));
        }

        private static IEnumerable<KeyValuePair<MethodDefinition, int>> InitializeOnLoadMethods(ModuleDefinition module)
        {
            foreach (var type in module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (!method.IsStatic || method.HasParameters || !method.HasBody) continue;
                    var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "RuntimeInitializeOnLoadMethodAttribute");
                    if (attribute == null) continue;
                    int loadType = attribute.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is int value ? value : 0;
                    yield return new KeyValuePair<MethodDefinition, int>(method, loadType);
                }
            }
        }

        private static MethodDefinition FindTarget(ModuleDefinition module)
        {
            var candidates = InitializeOnLoadMethods(module).Where(m => m.Value == SubsystemRegistration).Select(m => m.Key).ToList();
            return candidates.FirstOrDefault(m => m.DeclaringType.Name == "GameStateManager" && m.Name == "Init")
                   ?? candidates.OrderBy(m => m.DeclaringType.FullName, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault();
        }

        // The earliest method that runs after all SubsystemRegistration methods
        private static bool FindLateTarget(ModuleDefinition module, out MethodDefinition target, out string phase)
        {
            var methods = InitializeOnLoadMethods(module).ToList();
            for (int i = 0; i < LatePhases.Length; i++)
            {
                target = methods.Where(m => m.Value == LatePhases[i]).Select(m => m.Key)
                    .OrderBy(m => m.DeclaringType.FullName, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault();
                if (target == null) continue;
                phase = LatePhaseNames[i];
                return true;
            }
            target = null;
            phase = null;
            return false;
        }

        private static DefaultAssemblyResolver CreateResolver(string managedDir)
        {
            var resolver = new DefaultAssemblyResolver();
            if (!string.IsNullOrEmpty(managedDir) && Directory.Exists(managedDir)) resolver.AddSearchDirectory(managedDir);
            return resolver;
        }
    }
}
