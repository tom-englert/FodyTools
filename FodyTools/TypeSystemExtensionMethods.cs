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
        #region Event
        #region Event Add

        [CanBeNull]
        public static MethodReference TryImportEventAdd([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name)
        {
            return TryImport(typeSystem, declaringType, name, t => t.Events, e => e.AddMethod);
        }

        [CanBeNull]
        public static MethodReference TryImportEventAdd<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.TryImportEventAdd(typeof(T), name);
        }

        [CanBeNull]
        public static MethodReference TryImportEventAdd<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMemberInfo(expression, out var declaringType, out var name);

            return typeSystem.TryImportEventAdd(declaringType, name);
        }

        [NotNull]
        public static MethodReference ImportEventAdd([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name)
        {
            return typeSystem.TryImportEventAdd(declaringType, name) ?? throw new WeavingException($"Can't find add method for event {name} on type {declaringType}");
        }

        [NotNull]
        public static MethodReference ImportEventAdd<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.ImportEventAdd(typeof(T), name);
        }

        [NotNull]
        public static MethodReference ImportEventAdd<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMemberInfo(expression, out var declaringType, out var name);

            return typeSystem.ImportEventAdd(declaringType, name);
        }

        #endregion
        #region Event Remove

        [CanBeNull]
        public static MethodReference TryImportEventRemove([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name)
        {
            return TryImport(typeSystem, declaringType, name, t => t.Events, e => e.RemoveMethod);
        }

        [CanBeNull]
        public static MethodReference TryImportEventRemove<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.TryImportEventRemove(typeof(T), name);
        }

        [CanBeNull]
        public static MethodReference TryImportEventRemove<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMemberInfo(expression, out var declaringType, out var name);

            return typeSystem.TryImportEventRemove(declaringType, name);
        }

        [NotNull]
        public static MethodReference ImportEventRemove([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name)
        {
            return typeSystem.TryImportEventRemove(declaringType, name) ?? throw new WeavingException($"Can't find remove for event {name} on type {declaringType}");
        }

        [NotNull]
        public static MethodReference ImportEventRemove<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.ImportEventRemove(typeof(T), name);
        }

        [NotNull]
        public static MethodReference ImportEventRemove<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMemberInfo(expression, out var declaringType, out var name);

            return typeSystem.ImportEventRemove(declaringType, name);
        }

        #endregion
        #endregion

        #region Property
        #region Property Get
        
        [CanBeNull]
        public static MethodReference TryImportPropertyGet([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name)
        {
            return TryImport(typeSystem, declaringType, name, t => t.Properties, p => p.GetMethod);
        }

        [CanBeNull]
        public static MethodReference TryImportPropertyGet<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.TryImportPropertyGet(typeof(T), name);
        }

        [CanBeNull]
        public static MethodReference TryImportPropertyGet<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMemberInfo(expression, out var declaringType, out var name);

            return typeSystem.TryImportPropertyGet(declaringType, name);
        }

        [NotNull]
        public static MethodReference ImportPropertyGet([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name)
        {
            return typeSystem.TryImportPropertyGet(declaringType, name) ?? throw new WeavingException($"Can't find getter for property {name} on type {declaringType}");
        }

        [NotNull]
        public static MethodReference ImportPropertyGet<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.ImportPropertyGet(typeof(T), name);
        }

        [NotNull]
        public static MethodReference ImportPropertyGet<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMemberInfo(expression, out var declaringType, out var name);

            return typeSystem.ImportPropertyGet(declaringType, name);
        }

        #endregion
        #region Property Set

        [CanBeNull]
        public static MethodReference TryImportPropertySet([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name)
        {
            return TryImport(typeSystem, declaringType, name, t => t.Properties, p => p.SetMethod);
        }

        [CanBeNull]
        public static MethodReference TryImportPropertySet<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.TryImportPropertySet(typeof(T), name);
        }

        [CanBeNull]
        public static MethodReference TryImportPropertySet<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMemberInfo(expression, out var declaringType, out var name);

            return typeSystem.TryImportPropertySet(declaringType, name);
        }

        [NotNull]
        public static MethodReference ImportPropertySet([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name)
        {
            return typeSystem.TryImportPropertySet(declaringType, name) ?? throw new WeavingException($"Can't find setter for property {name} on type {declaringType}");
        }

        [NotNull]
        public static MethodReference ImportPropertySet<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.ImportPropertySet(typeof(T), name);
        }

        [NotNull]
        public static MethodReference ImportPropertySet<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMemberInfo(expression, out var declaringType, out var name);

            return typeSystem.ImportPropertySet(declaringType, name);
        }

        #endregion
        #endregion

        #region Method

        [CanBeNull]
        private static MethodReference TryImportMethod([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name, [NotNull, ItemNotNull] Type[] argumentTypes)
        {
            return TryImport(typeSystem, declaringType, name, t => t.Methods, m => ParametersMatch(m.Parameters, argumentTypes), m => m);
        }

        [NotNull]
        private static MethodReference ImportMethod([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name, [NotNull, ItemNotNull] Type[] argumentTypes)
        {
            return TryImport(typeSystem, declaringType, name, t => t.Methods, m => ParametersMatch(m.Parameters, argumentTypes), m => m)
                   ?? throw new WeavingException($"Can't find method {name}({string.Join(", ", (IEnumerable<Type>)argumentTypes)}) on type {declaringType}");
        }

        [CanBeNull]
        public static MethodReference TryImportMethod<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name, [NotNull, ItemNotNull] params Type[] argumentTypes)
        {
            return typeSystem.TryImportMethod(typeof(T), name, argumentTypes);
        }

        [NotNull]
        public static MethodReference ImportMethod<T>([NotNull] this ITypeSystem typeSystem, [NotNull] string name, [NotNull, ItemNotNull] params Type[] argumentTypes)
        {
            return typeSystem.ImportMethod(typeof(T), name, argumentTypes);
        }

        [CanBeNull]
        public static MethodReference TryImportMethod<T, TP1>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.TryImportMethod<T>(name, typeof(TP1));
        }

        [NotNull]
        public static MethodReference ImportMethod<T, TP1>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.ImportMethod<T>(name, typeof(TP1));
        }

        [CanBeNull]
        public static MethodReference TryImportMethod<T, TP1, TP2>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.TryImportMethod<T>(name, typeof(TP1), typeof(TP2));
        }

        [NotNull]
        public static MethodReference ImportMethod<T, TP1, TP2>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.ImportMethod<T>(name, typeof(TP1), typeof(TP2));
        }

        [CanBeNull]
        public static MethodReference TryImportMethod<T, TP1, TP2, TP3>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.TryImportMethod<T>(name, typeof(TP1), typeof(TP2), typeof(TP3));
        }

        [NotNull]
        public static MethodReference ImportMethod<T, TP1, TP2, TP3>([NotNull] this ITypeSystem typeSystem, [NotNull] string name)
        {
            return typeSystem.ImportMethod<T>(name, typeof(TP1), typeof(TP2), typeof(TP3));
        }

        [CanBeNull]
        public static MethodReference TryImportMethod<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMethodInfo(expression, out var declaringType, out var methodName, out var argumentTypes);

            return typeSystem.TryImportMethod(declaringType, methodName, argumentTypes);
        }

        [NotNull]
        public static MethodReference ImportMethod<TResult>([NotNull] this ITypeSystem typeSystem, [NotNull] Expression<Func<TResult>> expression)
        {
            GetMethodInfo(expression, out var declaringType, out var methodName, out var argumentTypes);

            return typeSystem.ImportMethod(declaringType, methodName, argumentTypes);
        }

        #endregion

        #region Type

        [NotNull]
        public static TypeReference ImportType([NotNull] this ITypeSystem typeSystem, Type type)
        {
            return typeSystem.ModuleDefinition.ImportReference(typeSystem.FindType(GetFullName(type)));
        }

        [CanBeNull]
        public static TypeReference TryImportType([NotNull] this ITypeSystem typeSystem, Type type)
        {
            if (!typeSystem.TryFindType(GetFullName(type), out var typeDefinition))
                return null;

            return typeSystem.ModuleDefinition.ImportReference(typeDefinition);
        }

        [NotNull]
        public static TypeReference ImportType<T>([NotNull] this ITypeSystem typeSystem)
        {
            return typeSystem.ImportType(typeof(T));
        }

        [CanBeNull]
        public static TypeReference TryImportType<T>([NotNull] this ITypeSystem typeSystem)
        {
            return typeSystem.TryImportType(typeof(T));
        }

        #endregion

        [NotNull]
        private static string GetFullName([NotNull] Type type)
        {
            // type.FullName may contain extra generic info!
            return type.Namespace + "." + type.Name;
        }

        private static void GetMemberInfo<TResult>([NotNull] Expression<Func<TResult>> expression, [NotNull] out Type declaringType, [NotNull] out string memberName)
        {
            switch (expression.Body)
            {
                case MemberExpression memberExpression:
                    memberName = memberExpression.Member.Name;
                    declaringType = memberExpression.Member.DeclaringType;
                    break;

                default:
                    throw new ArgumentException("Only member expression is supported.", nameof(expression));
            }
        }

        private static void GetMethodInfo<TResult>([NotNull] Expression<Func<TResult>> expression, out Type declaringType, [NotNull] out string methodName, [NotNull] out Type[] argumentTypes)
        {
            switch (expression.Body)
            {
                case NewExpression newExpression:
                    methodName = ".ctor";
                    declaringType = newExpression.Type;
                    argumentTypes = newExpression.Arguments.Select(a => a.Type).ToArray();
                    break;

                case MethodCallExpression methodCall:
                    methodName = methodCall.Method.Name;
                    declaringType = methodCall.Method.DeclaringType;
                    argumentTypes = methodCall.Arguments.Select(a => a.Type).ToArray();
                    break;

                default:
                    throw new ArgumentException("Only method call or new expression is supported.", nameof(expression));
            }
        }

        private static bool ParametersMatch([NotNull] IList<ParameterDefinition> parameters, [NotNull] IList<Type> argumentTypes)
        {
            if (parameters.Count != argumentTypes.Count)
                return false;

            var genericParameterMap = new Dictionary<string, Type>();

            for (var i = 0; i < parameters.Count; i++)
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
                else if (parameterType.GetElementType().FullName != GetFullName(argumentType))
                {
                    return false;
                }
            }

            return true;
        }

        private static MethodReference TryImport<T>([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name, [NotNull] Func<TypeDefinition, IEnumerable<T>> elementLookup, [NotNull] Func<T, MethodDefinition> selector)
            where T : class, IMemberDefinition
        {
            return TryImport(typeSystem, declaringType, name, elementLookup, _ => true, selector);
        }

        private static MethodReference TryImport<T>([NotNull] this ITypeSystem typeSystem, [NotNull] Type declaringType, [NotNull] string name, [NotNull] Func<TypeDefinition, IEnumerable<T>> elementLookup, [NotNull] Func<T, bool> constraints, [NotNull] Func<T, MethodDefinition> selector)
            where T : class, IMemberDefinition
        {
            if (!typeSystem.TryFindType(GetFullName(declaringType), out var typeDefinition))
                return null;

            var method = elementLookup(typeDefinition).Where(p => p.Name == name).Where(constraints).Select(selector).SingleOrDefault();

            if (method == null)
                return null;

            return typeSystem.ModuleDefinition.ImportReference(method);
        }
    }
}