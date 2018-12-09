using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerformanceTest
{
    using System.Diagnostics;
    using System.IO;

    using FodyTools;
    using FodyTools.Tests;

    using JetBrains.Annotations;

    using Mono.Cecil;

    using Xunit;

    class Program
    {
        static void Main(string[] args)
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            var assemblyPath = Path.Combine(baseDirectory, "ShellAssembly.exe");
            var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { ReadSymbols = true });

            var codeImporter = new CodeImporter(module)
            {
                HideImportedTypes = false,
                ModuleResolver = new AssemblyModuleResolver(
                    typeof(Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialog).Assembly, 
                    typeof(Microsoft.WindowsAPICodePack.Dialogs.TaskDialog).Assembly,
                    typeof(Newtonsoft.Json.JsonConvert).Assembly)
            };

            codeImporter.ILMerge();

            var tempPath = TestHelper.TempPath;

            var importedModules = codeImporter.ListImportedModules();

            foreach (var m in importedModules)
            {
                foreach (var resource in m.Resources.OfType<EmbeddedResource>())
                {
                    module.Resources.Add(resource);
                }
            }

            var targetAssemblyPath = Path.Combine(tempPath, "ShellAssembly2.exe");

            module.Assembly.Name.Name = "ShellAssembly2";
            var now = DateTime.Now;
            module.Assembly.Name.Version = new Version(now.Year, now.Month, now.Day, (int)now.TimeOfDay.TotalMilliseconds);
            module.Write(targetAssemblyPath);

            var peVerify = TestHelper.PEVerify.Verify(targetAssemblyPath, line => Console.WriteLine(line));

            var importedTypes = codeImporter.ListImportedTypes();

            TestHelper.VerifyTypes(importedTypes, importedModules, targetAssemblyPath, AssertIl);

            var il = TestHelper.ILDasm.Decompile(targetAssemblyPath);

            File.WriteAllText(Path.ChangeExtension(targetAssemblyPath, ".il"), il);

            Console.WriteLine("Done - press any key...");
            Console.ReadKey();

            Assert.True(peVerify);
        }

        private static void AssertIl([NotNull] string typeName, [NotNull] string source, [NotNull] string target)
        {
            switch (typeName)
            {
                case "Microsoft.WindowsAPICodePack.Shell.ShellNativeMethods":
                case "MS.WindowsAPICodePack.Internal.CoreNativeMethods":
                case "Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties":
                    AssertIlIsSubset(source, target);
                    break;

                default:
                    TestHelper.AssertIlStrict(source, target);
                    break;
            }
        }

        private static void AssertIlIsSubset([NotNull] string source, [NotNull] string target)
        {
            var tempPath = TestHelper.TempPath;

            File.WriteAllText(Path.Combine(tempPath, "source_1.txt"), string.Join("\n\n", source.Replace("\r\n", "\n").Replace("\n{\n", "\n{\n\n").Split(new[] { "\n\n" }, StringSplitOptions.None).OrderBy(para => para.Split('\n').First().Split(' ').Last())));
            File.WriteAllText(Path.Combine(tempPath, "target_1.txt"), string.Join("\n\n", target.Replace("\r\n", "\n").Replace("\n{\n", "\n{\n\n").Split(new[] { "\n\n" }, StringSplitOptions.None).OrderBy(para => para.Split('\n').First().Split(' ').Last())));

            var sourceHash = new HashSet<string>(source.Replace("\r\n", "\n").Split('\n'));

            if (!target.Replace("\r\n", "\n").Split('\n').All(line => sourceHash.Contains(line)))
            {
                Debugger.Break();
            }
        }
    }
}
