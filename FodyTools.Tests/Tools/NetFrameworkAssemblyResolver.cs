using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using Mono.Cecil;
using TomsToolbox.Core;

namespace FodyTools.Tests.Tools
{
    public class NetFrameworkAssemblyResolver : IAssemblyResolver
    {
        private static readonly string _refAssembliesFolder = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2");
        private readonly Dictionary<string, AssemblyDefinition> _cache = new Dictionary<string, AssemblyDefinition>();
        private readonly IAssemblyResolver _defaultResolver = new DefaultAssemblyResolver();

        public static readonly IAssemblyResolver Default = new NetFrameworkAssemblyResolver();

#if NETFRAMEWORK
        public static readonly IAssemblyResolver Current = Default;
#else
        public static readonly IAssemblyResolver Current = null;
#endif

        public void Dispose()
        {
        }

        [CanBeNull]
        public AssemblyDefinition Resolve(AssemblyNameReference nameReference)
        {
            return Resolve(nameReference, new ReaderParameters { AssemblyResolver = this });
        }

        [CanBeNull]
        public AssemblyDefinition Resolve(AssemblyNameReference nameReference, ReaderParameters parameters)
        {
            var name = nameReference.Name;

            var path = Path.Combine(_refAssembliesFolder, name + ".dll");
            if (!File.Exists(path))
                return _defaultResolver.Resolve(nameReference, parameters);

            return _cache.ForceValue(name, _ => AssemblyDefinition.ReadAssembly(path, parameters));
        }
    }
}
