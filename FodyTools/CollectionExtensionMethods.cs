namespace FodyTools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using JetBrains.Annotations;

    internal static class CollectionExtensionMethods
    {
        public static void AddRange<T>([NotNull, ItemCanBeNull] this IList<T> collection, [NotNull, ItemCanBeNull] params T[] values)
        {
            AddRange(collection, (IEnumerable<T>)values);
        }

        public static void AddRange<T>([NotNull, ItemCanBeNull] this IList<T> collection, [NotNull, ItemCanBeNull] IEnumerable<T> values)
        {
            foreach (var value in values)
            {
                collection.Add(value);
            }
        }

        public static void InsertRange<T>([NotNull, ItemCanBeNull] this IList<T> collection, int index, [NotNull, ItemCanBeNull] params T[] values)
        {
            InsertRange(collection, index, (IEnumerable<T>)values);
        }

        public static void InsertRange<T>([NotNull, ItemCanBeNull] this IList<T> collection, int index, [NotNull, ItemCanBeNull] IEnumerable<T> values)
        {
            foreach (var value in values)
            {
                collection.Insert(index++, value);
            }
        }

        public static void Replace<T>([CanBeNull, ItemCanBeNull] this IList<T> collection, [CanBeNull, ItemCanBeNull] IEnumerable<T> values)
        {
            if ((collection == null) || (values == null))
                return;

            collection.Clear();
            collection.AddRange(values);
        }

        public static void RemoveAll<T>([NotNull, ItemCanBeNull] this ICollection<T> target, [NotNull] Func<T, bool> condition)
        {
            target.RemoveAll(target.Where(condition).ToList());
        }

        public static void RemoveAll<T>([NotNull, ItemCanBeNull] this ICollection<T> target, [NotNull, ItemCanBeNull] IEnumerable<T> items)
        {
            foreach (var i in items)
            {
                target.Remove(i);
            }
        }

        [NotNull]
        public static IReadOnlyList<T> ToReadOnlyList<T>([NotNull, ItemCanBeNull] this IEnumerable<T> items)
        {
            return items.ToList().AsReadOnly();
        }
    }
}
