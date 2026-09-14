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
        private const int SubsystemRegistration = 4;

        // Returns the patched assembly bytes and a description of the method that now calls the loader
        public static byte[] Patch(byte[] original, string managedDir, string loaderDllPath, out string targetDescription)
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
                var initRef = module.ImportReference(init);
                var il = target.Body.GetILProcessor();
                il.InsertBefore(target.Body.Instructions[0], il.Create(OpCodes.Call, initRef));

                targetDescription = target.DeclaringType.FullName + "." + target.Name;
                using (var output = new MemoryStream())
                {
                    assembly.Write(output);
                    return output.ToArray();
                }
            }
        }

        private static MethodDefinition FindTarget(ModuleDefinition module)
        {
            var candidates = new List<MethodDefinition>();
            foreach (var type in module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (!method.IsStatic || method.HasParameters || !method.HasBody) continue;
                    var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "RuntimeInitializeOnLoadMethodAttribute");
                    if (attribute == null) continue;
                    int loadType = attribute.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is int value ? value : 0;
                    if (loadType == SubsystemRegistration) candidates.Add(method);
                }
            }

            return candidates.FirstOrDefault(m => m.DeclaringType.Name == "GameStateManager" && m.Name == "Init")
                   ?? candidates.OrderBy(m => m.DeclaringType.FullName, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault();
        }

        private static DefaultAssemblyResolver CreateResolver(string managedDir)
        {
            var resolver = new DefaultAssemblyResolver();
            if (!string.IsNullOrEmpty(managedDir) && Directory.Exists(managedDir)) resolver.AddSearchDirectory(managedDir);
            return resolver;
        }
    }
}
