namespace FodyTools.Tests.Tools
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;

    using Mono.Cecil;
    using Mono.Cecil.Rocks;

    internal static class ModuleHelper
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
            var module = ModuleDefinition.ReadModule(fileName, new ReaderParameters { ReadSymbols = true });
            return module;
        }

        public static TypeDefinition LoadType<T>()
        {
            return LoadType(typeof(T));
        }

        public static TypeDefinition LoadType(this Type declaringType)
        {
            var module = LoadModule(declaringType);
            var type = module.GetTypes().Single(t => t.FullName == declaringType.FullName)
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
    }
}
