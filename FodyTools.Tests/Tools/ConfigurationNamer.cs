using ApprovalTests.Reporters;

[assembly: UseReporter(typeof(DiffReporter))]

namespace FodyTools.Tests.Tools
{
    using System;
    using System.Linq;

    using ApprovalTests;
    using ApprovalTests.Namers;

    public class ConfigurationNamer : UnitTestFrameworkNamer
    {
        private static string Configuration()
        {
#if DEBUG
            return "Debug";
#else
            return "Release";
#endif
        }

        static ConfigurationNamer()
        {
            Approvals.RegisterDefaultNamerCreation(() => new ConfigurationNamer());
        }

        public static void Register()
        {
        }

        public override string Name => base.Name + "." + Configuration();

        public override string SourcePath
        {
            get
            {
                Console.WriteLine("Stack: " + Approvals.CurrentCaller.StackTrace);
                Console.WriteLine("Files: \n  " + string.Join("\n  ", Approvals.CurrentCaller.StackTrace.GetFrames().Select(s => s.GetFileName())));
                Console.WriteLine("Parser: " + stackTraceParser.parser.ForTestingFramework);
                Console.WriteLine("Source Path: " + base.SourcePath);
                return base.SourcePath;
            }
        }
    }
}