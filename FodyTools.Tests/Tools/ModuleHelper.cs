﻿namespace FodyTools.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Runtime.Versioning;

    using JetBrains.Annotations;

    using Mono.Cecil;
    using Mono.Cecil.Rocks;

    public static class ModuleHelper
    {
        public static string GetModuleFileName<T>()
        {
            return GetModuleFileName(typeof(T));
        }

        public static string GetModuleFileName(this Type containingType)
        {
            return new Uri(containingType.Assembly.CodeBase).LocalPath;
        }

        public static string GetModuleFolder<T>()
        {
            return GetModuleFolder(typeof(T));
        }

        public static string GetModuleFolder(this Type containingType)
        {
            return Path.GetDirectoryName(GetModuleFileName(containingType)) ?? throw new InvalidOperationException();
        }

        public static ModuleDefinition LoadModule<T>()
        {
            return LoadModule(typeof(T));
        }

        public static ModuleDefinition LoadModule(this Type containingType)
        {
            var fileName = GetModuleFileName(containingType);

            return LoadModule(fileName);
        }

        public static ModuleDefinition LoadModule(string fileName)
        {
            var assemblyResolver = new AssemblyResolverAdapter();
            var module = ModuleDefinition.ReadModule(fileName, new ReaderParameters { ReadSymbols = false, AssemblyResolver = assemblyResolver });
            assemblyResolver.Init(module.GetTargetFrameworkName());
            try
            {
                module.ReadSymbols();
            }
            catch
            {
                // just go without symbols...
            }
            return module;
        }

        public static TypeDefinition LoadType<T>()
        {
            return LoadType(typeof(T));
        }

        public static TypeDefinition LoadType(this Type declaringType)
        {
            var module = LoadModule(declaringType);
            var type = module.GetTypes().Single(t => t.FullName == declaringType.FullName?.Replace("+", "/"))
                       ?? throw new InvalidOperationException($"Type {declaringType} not found in module {module.FileName}");

            return type;
        }

        public static MethodDefinition LoadMethod(Expression<Action> expression)
        {
            expression.GetMethodInfo(out var declaringType, out var methodName, out var argumentTypes);

            var type = LoadType(declaringType);
            var method = type.GetMethods().Single(m => (m.Name == methodName) && (m.Parameters.ParametersMatch(argumentTypes)))
                         ?? throw new InvalidOperationException($"Method {methodName}({string.Join(", ", argumentTypes)}) not found on type {declaringType}");

            return method;
        }

        [CanBeNull]
        public static FrameworkName GetTargetFrameworkName([NotNull] this ModuleDefinition moduleDefinition)
        {
            return moduleDefinition.Assembly
                .CustomAttributes
                .Where(attr => attr.AttributeType.FullName == typeof(TargetFrameworkAttribute).FullName)
                .Select(attr => attr.ConstructorArguments.Select(arg => arg.Value as string).FirstOrDefault())
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => new FrameworkName(name))
                .FirstOrDefault();
        }

        [CanBeNull]
        public static FrameworkName GetTargetFrameworkName([NotNull] this Type typeInTargetAssembly)
        {
            return typeInTargetAssembly.Assembly
                .CustomAttributes
                .Where(attr => attr.AttributeType.FullName == typeof(TargetFrameworkAttribute).FullName)
                .Select(attr => attr.ConstructorArguments.Select(arg => arg.Value as string).FirstOrDefault())
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => new FrameworkName(name))
                .FirstOrDefault();
        }

        public static IAssemblyResolver AssemblyResolver => new AssemblyResolverAdapter(typeof(ModuleHelper).GetTargetFrameworkName());

        private interface IInternalAssemblyResolver
        {
            [CanBeNull]
            AssemblyDefinition Resolve([NotNull] AssemblyNameReference nameReference, [NotNull] ReaderParameters parameters);
        }

        private class AssemblyResolverAdapter : IAssemblyResolver
        {
            private readonly IAssemblyResolver _defaultResolver = new DefaultAssemblyResolver();
            [CanBeNull]
            private IInternalAssemblyResolver _internalResolver;

            public AssemblyResolverAdapter()
            {
            }

            public AssemblyResolverAdapter([CanBeNull] FrameworkName frameworkName)
            {
                Init(frameworkName);
            }

            public void Init([CanBeNull] FrameworkName frameworkName)
            {
                switch (frameworkName?.Identifier)
                {
                    case ".NETFramework":
                        _internalResolver = new NetFrameworkAssemblyResolver(frameworkName.Version);
                        break;
                }
            }

            [CanBeNull]
            public AssemblyDefinition Resolve([NotNull] AssemblyNameReference nameReference)
            {
                return Resolve(nameReference, new ReaderParameters());
            }

            [CanBeNull]
            public AssemblyDefinition Resolve([NotNull] AssemblyNameReference name, [NotNull] ReaderParameters parameters)
            {
                if (parameters.AssemblyResolver == null)
                    parameters.AssemblyResolver = this;

                return _internalResolver?.Resolve(name, parameters) ?? _defaultResolver.Resolve(name, parameters);
            }

            public void Dispose()
            {
                _defaultResolver.Dispose();
            }
        }

        private class NetFrameworkAssemblyResolver : IInternalAssemblyResolver
        {
            private readonly string _refAssembliesFolder;
            private readonly Dictionary<string, AssemblyDefinition> _cache = new Dictionary<string, AssemblyDefinition>();

            public NetFrameworkAssemblyResolver(Version frameworkVersion)
            {
                _refAssembliesFolder = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v" + frameworkVersion);
            }

            [CanBeNull]
            public AssemblyDefinition Resolve([NotNull] AssemblyNameReference nameReference, [NotNull] ReaderParameters parameters)
            {
                var name = nameReference.Name;

                if (_cache.TryGetValue(name, out var definition))
                    return definition;

                var path = Path.Combine(_refAssembliesFolder, name + ".dll");
                if (!File.Exists(path))
                    return null;

                var assemblyDefinition = AssemblyDefinition.ReadAssembly(path, parameters);

                _cache.Add(name, assemblyDefinition);

                return assemblyDefinition;
            }
        }
    }
}
