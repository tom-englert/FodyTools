using ApprovalTests.Reporters;

[assembly: UseReporter(typeof(DiffReporter))]

namespace FodyTools.Tests.Tools
{
    using ApprovalTests;
    using ApprovalTests.Namers;

    public class ConfigurationNamer : UnitTestFrameworkNamer
    {
        private static readonly string _suffix =
#if DEBUG
            ".Debug";
#else
            ".Release";
#endif

        static ConfigurationNamer()
        {
            Approvals.RegisterDefaultNamerCreation(() => new ConfigurationNamer());
        }

        public static void Register()
        {
        }

        public override string Name => base.Name + _suffix;
    }
}