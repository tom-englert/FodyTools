namespace FodyTools
{
    using System.Collections.Generic;
    using System.Linq;

    using JetBrains.Annotations;

    using Mono.Cecil;

    internal static class AttributeExtensionMethods
    {
        [CanBeNull]
        public static CustomAttribute GetAttribute([CanBeNull] this ICustomAttributeProvider attributeProvider, [CanBeNull] string attributeName)
        {
            return attributeProvider?.CustomAttributes.GetAttribute(attributeName);
        }

        [CanBeNull]
        public static CustomAttribute GetAttribute([CanBeNull] this IEnumerable<CustomAttribute> attributes, [CanBeNull] string attributeName)
        {
            return attributes?.FirstOrDefault(attribute => attribute.Constructor.DeclaringType.FullName == attributeName);
        }

        [CanBeNull]
        public static T GetReferenceTypeConstructorArgument<T>([CanBeNull] this CustomAttribute attribute)
            where T : class
        {
            return attribute?.ConstructorArguments?
                .Select(arg => arg.Value as T)
                .FirstOrDefault(value => value != null);
        }

        public static T? GetValueTypeConstructorArgument<T>([CanBeNull] this CustomAttribute attribute)
            where T : struct
        {
            return attribute?.ConstructorArguments?
                .Select(arg => arg.Value as T?)
                .FirstOrDefault(value => value != null);
        }

        public static T GetPropertyValue<T>([NotNull] this CustomAttribute attribute, [CanBeNull] string propertyName, T defaultValue)
        {
            return attribute.Properties.Where(p => p.Name == propertyName)
                .Select(p => (T)p.Argument.Value)
                .DefaultIfEmpty(defaultValue)
                .Single();
        }
    }
}
