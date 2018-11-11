namespace FodyTools
{
    using System;

    using JetBrains.Annotations;

    using Mono.Cecil;

    internal static class MemberExtensionMethods
    {
        [NotNull]
        public static MethodReference OnGenericType([NotNull] this MethodReference method, [NotNull] TypeReference genericType)
        {
            if (!genericType.IsGenericInstance)
                throw new InvalidOperationException("Need a generic type as the target.");
            if (method.DeclaringType.Resolve() != genericType.Resolve())
                throw new InvalidOperationException("Generic type must resolve to the same type as the methods current type.");

            method = method.Module.ImportReference(method.Resolve());
            method.DeclaringType = genericType;

            return method;
        }
    }
}
