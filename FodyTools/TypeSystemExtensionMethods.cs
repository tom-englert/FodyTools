namespace FodyTools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;

    using Fody;

    using JetBrains.Annotations;

    using Mono.Cecil;

    internal static class TypeSystemExtensionMethods
    {
        [NotNull]
        public static MethodReference ImportMethod<T>([NotNull] this BaseModuleWeaver weaver, [NotNull] string name, [NotNull, ItemNotNull] params Type[] parameters)
        {
            var typeDefinition = weaver.FindType(typeof(T).FullName);

            return weaver.ModuleDefinition.ImportReference(typeDefinition.FindMethod(name, parameters));
        }

        [NotNull]
        public static MethodReference ImportMethod<T, TP1>([NotNull] this BaseModuleWeaver weaver, [NotNull] string name)
        {
            return ImportMethod<T>(weaver, name, typeof(TP1));
        }

        [NotNull]
        public static MethodReference ImportMethod<T, TP1, TP2>([NotNull] this BaseModuleWeaver weaver, [NotNull] string name)
        {
            return ImportMethod<T>(weaver, name, typeof(TP1), typeof(TP2));
        }

        [NotNull]
        public static MethodReference ImportMethod<T, TP1, TP2, TP3>([NotNull] this BaseModuleWeaver weaver, [NotNull] string name)
        {
            return ImportMethod<T>(weaver, name, typeof(TP1), typeof(TP2), typeof(TP3));
        }

        public static MethodReference ImportMethod<TResult>([NotNull] this BaseModuleWeaver weaver, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMethodInfo(expression, out var methodName, out var declaringTypeName, out var argumentTypes);

            var typeDefinition = weaver.FindType(declaringTypeName);

            try
            {
                var method = typeDefinition.Methods
                    .Single(m => m.Name == methodName && ParametersMatch(m.Parameters, argumentTypes));

                return weaver.ModuleDefinition.ImportReference(method);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"Method {methodName} does not exist on type {declaringTypeName}", ex);
            }
        }

        public static MethodReference TryImportMethod<TResult>([NotNull] this BaseModuleWeaver weaver, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMethodInfo(expression, out var methodName, out var declaringTypeName, out var argumentTypes);

            if (!weaver.TryFindType(declaringTypeName, out var typeDefinition))
                return null;

            var method = typeDefinition.Methods
                .FirstOrDefault(m => m.Name == methodName && ParametersMatch(m.Parameters, argumentTypes));

            if (method == null)
                return null;

            return weaver.ModuleDefinition.ImportReference(method);
        }

        public static TypeReference ImportType<T>([NotNull] this BaseModuleWeaver weaver)
        {
            return weaver.ModuleDefinition.ImportReference(weaver.FindType(typeof(T).FullName));
        }

        public static TypeReference TryImportType<T>([NotNull] this BaseModuleWeaver weaver)
        {
            if (!weaver.TryFindType(typeof(T).FullName, out var typeDefinition))
                return null;

            return weaver.ModuleDefinition.ImportReference(typeDefinition);
        }

        private static void GetMethodInfo<TResult>([NotNull] Expression<Func<TResult>> expression, out string methodName, out string declaringTypeName, out Type[] argumentTypes)
        {
            if (!(expression.Body is MethodCallExpression methodCall))
                throw new ArgumentException("Only method call expression is supported.", nameof(expression));

            methodName = methodCall.Method.Name;
            declaringTypeName = methodCall.Method.DeclaringType.FullName;
            argumentTypes = methodCall.Arguments.Select(a => a.Type).ToArray();
        }

        [NotNull]
        private static MethodDefinition FindMethod([NotNull] this TypeDefinition type, [NotNull] string name, [NotNull, ItemNotNull] params Type[] parameters)
        {
            return type.Methods.First(x => (x.Name == name) && x.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters.Select(p => p.FullName)));
        }

        private static bool ParametersMatch([NotNull] IList<ParameterDefinition> parameters, [NotNull] IList<Type> argumentTypes)
        {
            if (parameters.Count != argumentTypes.Count)
                return false;

            var genericParameterMap = new Dictionary<string, Type>();

            for (int i = 0; i < parameters.Count; i++)
            {
                var parameterType = parameters[i].ParameterType;
                var argumentType = argumentTypes[i];

                if (parameterType.ContainsGenericParameter)
                {
                    // for generic parameters just verify that every generic type matches to the same placeholder type.
                    var elementTypeName = parameterType.GetElementType().FullName;

                    if (genericParameterMap.TryGetValue(elementTypeName, out var mappedType))
                    {
                        if (mappedType != argumentType)
                            return false;
                    }
                    else 
                    {
                        genericParameterMap.Add(elementTypeName, argumentType);
                    }
                }
                else if (parameterType.GetElementType().FullName != argumentType.FullName)
                {
                    return false;
                }
            }

            return true;
        }
    }
}