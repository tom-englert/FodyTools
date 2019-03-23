namespace FodyTools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using JetBrains.Annotations;

    using Mono.Cecil;
    using Mono.Cecil.Cil;

    internal static class MemberExtensionMethods
    {
        [NotNull]
        public static MethodReference OnGenericType([NotNull] this MethodReference method, [NotNull] TypeReference genericType)
        {
            if (!genericType.IsGenericInstance)
                throw new InvalidOperationException("Need a generic type as the target.");
            if (method.DeclaringType.Resolve() != genericType.Resolve())
                throw new InvalidOperationException("Generic type must resolve to the same type as the methods current type.");
            if (method.IsGenericInstance)
                throw new InvalidOperationException("method is already a generic instance");

            var newMethod = new MethodReference(method.Name, method.ReturnType, genericType)
            {
                CallingConvention = method.CallingConvention,
                ExplicitThis = method.ExplicitThis,
                HasThis = method.HasThis,
            };

            newMethod.Parameters.AddRange(method.Parameters);
            newMethod.GenericParameters.AddRange(method.Resolve().GenericParameters.Select(p => new GenericParameter(p.Name, p.Owner)));
            return newMethod;
        }

        public static GenericInstanceMethod MakeGenericInstanceMethod([NotNull] this MethodReference method, params TypeReference[] arguments)
        {
            var newMethod = new GenericInstanceMethod(method);

            if (method.GenericParameters.Count != arguments.Length)
                throw new InvalidOperationException("Generic argument mismatch");

            newMethod.GenericParameters.AddRange(method.Resolve().GenericParameters.Select(p => new GenericParameter(p.Name, p.Owner)));
            newMethod.GenericArguments.AddRange(arguments);

            return newMethod;
        }

        [CanBeNull]
        public static SequencePoint GetEntryPoint([CanBeNull] this MethodReference method, [CanBeNull] ISymbolReader symbolReader)
        {
            return method?.Resolve()?.ReadSequencePoints(symbolReader)?.FirstOrDefault();
        }

        [CanBeNull]
        public static IList<SequencePoint> ReadSequencePoints([CanBeNull] this MethodDefinition method, [CanBeNull] ISymbolReader symbolReader)
        {
            return (method?.DebugInformation?.HasSequencePoints == true)
                ? symbolReader?.Read(method)?.SequencePoints
                : null;
        }
    }
}
