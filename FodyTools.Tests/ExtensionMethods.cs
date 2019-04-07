namespace FodyTools.Tests
{
    using System.IO;

    using Fody;

    using JetBrains.Annotations;

    internal static class Directories
    {
        [NotNull]
        // ReSharper disable once AssignNullToNotNullAttribute
        public static string Target => Path.GetDirectoryName(typeof(Directories).Assembly.GetAssemblyLocation());
    }
}
