using ApprovalTests.Reporters;

[assembly: UseReporter(typeof(DiffReporter))]

namespace FodyTools.Tests.Tools
{
    using System;

    using ApprovalTests.Namers;

    public static class ConfigurationNamer
    {
        private static IDisposable _disposable = NamerFactory.AsEnvironmentSpecificTest(Configuration);

        private static string Configuration()
        {
#if DEBUG
            return "Debug";
#else
            return "Release";
#endif
        }

        public static void Register()
        {
        }
    }
}