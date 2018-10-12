namespace FodyTools.Tests
{
    using System.IO;

    using Fody;

    internal static class Directories
    {
        public static string Target => Path.GetDirectoryName(typeof(Directories).Assembly.GetAssemblyLocation());
    }
}
