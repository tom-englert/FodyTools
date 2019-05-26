namespace FodyTools.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    using JetBrains.Annotations;

    using Mono.Cecil;

    using TomsToolbox.Core;
    using TomsToolbox.Desktop;

    using Xunit;
    using Xunit.Abstractions;

    public static class TestHelper
    {
#if NETFRAMEWORK
        private const string Framework = "NET";
#else
        private const string Framework = "CORE";
#endif

        [NotNull]
        public static string TempPath
        {
            get
            {

                var tempPath = Path.Combine(Path.GetTempPath(), "CodeImporter", Framework);
                Directory.CreateDirectory(tempPath);
                return tempPath;
            }
        }

        public static void VerifyTypes(
            [NotNull] IDictionary<string, TypeDefinition> importedTypes,
            [NotNull] ICollection<ModuleDefinition> importedModules,
            [NotNull] string targetAssemblyPath)
        {
            VerifyTypes(importedTypes, importedModules, targetAssemblyPath, (assemblyName, source, target) => AssertIlStrict(source, target));
        }

        public static void VerifyTypes(
            [NotNull] IDictionary<string, TypeDefinition> importedTypes,
            [NotNull] ICollection<ModuleDefinition> importedModules,
            [NotNull] string targetAssemblyPath,
            [NotNull] Action<string, string, string> assert)
        {
            var importedTypeMap = importedModules
                .SelectMany(m => m.Types)
                .GroupBy(t => t.FullName)
                .Where(g => g.Count() == 1)
                .ToDictionary(t => t.Key, t => t.Single());

            var assemblyPrefixes = importedModules
                .Select(m => $"[{m.Assembly.Name.Name}]")
                .ToReadOnlyList();

            var tempPath = TempPath;

            foreach (var type in importedTypes)
            {
                if (type.Key.StartsWith("<"))
                    continue;

                var assemblyPath = importedTypeMap.TryGetValue(type.Key, out var sourceType) ? sourceType.Module.FileName : targetAssemblyPath;

                var decompiled = ILDasm.Decompile(assemblyPath, type.Key);

                var decompiledSource = FixSourceNamespaces(assemblyPrefixes, FixIndenting(FixAttributeOrder(decompiled)));
                var decompiledTarget = FixSystemNamespaces(FixIndenting(FixAttributeOrder(ILDasm.Decompile(targetAssemblyPath, type.Key))));

                File.WriteAllText(Path.Combine(tempPath, "source.txt"), decompiledSource);
                File.WriteAllText(Path.Combine(tempPath, "target.txt"), decompiledTarget);

                assert(type.Key, decompiledSource, decompiledTarget);
            }
        }

        public static void AssertIlStrict([NotNull] string decompiledSource, [NotNull] string decompiledTarget)
        {
            var expected = decompiledSource.Replace("\r\n", "\n").Split('\n').OrderBy(line => line).SkipWhile(string.IsNullOrWhiteSpace);
            var target = decompiledTarget.Replace("\r\n", "\n").Split('\n').OrderBy(line => line).SkipWhile(string.IsNullOrWhiteSpace);

            var mismatches = Enumerate.AsTuples(expected, target)
                .Select((tuple, index) => new {tuple.Item1, tuple.Item2, index})
                .Where(tuple => tuple.Item1 != tuple.Item2)
                .ToList();

            Assert.Empty(mismatches);
        }

        [NotNull]
        private static string FixAttributeOrder([NotNull] string value)
        {
            return value.Replace(
                "  .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) \r\n  .custom instance void [mscorlib]System.Diagnostics.DebuggerBrowsableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggerBrowsableState) = ( 01 00 00 00 00 00 00 00 ) ",
                "  .custom instance void [mscorlib]System.Diagnostics.DebuggerBrowsableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggerBrowsableState) = ( 01 00 00 00 00 00 00 00 ) \r\n  .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) ");
        }

        [NotNull]
        private static string FixSourceNamespaces([NotNull] IEnumerable<string> assemblyPrefixes, [NotNull] string value)
        {
            value = assemblyPrefixes.Aggregate(value, (current, modulePrefix) => current.Replace(modulePrefix, string.Empty));

            return FixSystemNamespaces(value);
        }

        [NotNull]
        private static string FixSystemNamespaces([NotNull] string value)
        {
            var regex = new Regex(@"\[System\.[\.\w]+\]");

            var result = regex.Replace(value, "[System]");

            result = result.Replace("[mscorlib]", "[System]");

            return result;
        }

        [NotNull]
        private static string FixIndenting([NotNull] string value)
        {
            return string.Join(Environment.NewLine, value.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Select(TrimIndent));
        }

        [NotNull]
        private static string TrimIndent([NotNull] string line)
        {
            var spaces = new string(' ', 16);
            if (line.StartsWith(spaces))
                return line.TrimStart(' ');

            return line;
        }

        public static class PEVerify
        {
            private static readonly string _peVerifyPath = SdkTool.Find("PEVerify.exe");

            public static bool Verify(string assemblyPath, [NotNull] ITestOutputHelper testOutputHelper)
            {
                return Verify(assemblyPath, line => testOutputHelper.WriteLine(line));
            }

            public static bool Verify(string assemblyPath, [NotNull] Action<string> writeOutput)
            {
                var workingDirectory = Path.GetDirectoryName(assemblyPath);

                var ignoreCodes = new[]
                {
                    "0x80131869", // can't resolve reference => PEVerify can't find the referenced dll...
                    "0x80070002", // The system cannot find the file specified.
                    "0x801318F3"  // Type load failed 
                };

                var processStartInfo = new ProcessStartInfo(_peVerifyPath)
                {
                    Arguments = $"\"{assemblyPath}\" /hresult /VERBOSE /nologo /ignore={string.Join(",", ignoreCodes)}",
                    WorkingDirectory = workingDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using (var process = Process.Start(processStartInfo))
                {
                    var output = process.StandardOutput.ReadToEnd();

                    output = Regex.Replace(output, @"^All Classes and Methods.*", "");

                    if (!process.WaitForExit(10000))
                    {
                        throw new Exception("PeVerify failed to exit");
                    }

                    if (process.ExitCode != 0)
                    {
                        writeOutput(_peVerifyPath);
                        writeOutput(output);

                        return false;
                    }
                }

                return true;
            }
        }

        public static class ILDasm
        {
            private static readonly string _ilDasmPath = SdkTool.Find("ILDasm.exe");

            [NotNull]
            public static string Decompile(string assemblyPath)
            {
                var startInfo = new ProcessStartInfo(_ilDasmPath, $"\"{assemblyPath}\" /text")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    return process?.StandardOutput.ReadToEnd() ?? "ILDasm did not start";
                }
            }

            [NotNull]
            public static string Decompile(string assemblyPath, string className)
            {
                // var startInfo = new ProcessStartInfo(_ilDasmPath, $"\"{assemblyPath}\" /text /classlist /item:{className}")
                var startInfo = new ProcessStartInfo(_ilDasmPath, $"\"{assemblyPath}\" /text /item:{className}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    return process?.StandardOutput.ReadToEnd() ?? "ILDasm did not start";
                }
            }
        }

        private static class SdkTool
        {
            [NotNull]
            public static string Find([NotNull] string fileName)
            {
                var windowsSdkDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft SDKs\\Windows");


                var path = Directory.EnumerateFiles(windowsSdkDirectory, fileName, SearchOption.AllDirectories)
                               .Where(item => item?.IndexOf("x64", StringComparison.OrdinalIgnoreCase) == -1)
                               .OrderByDescending(GetFileVersion)
                               .FirstOrDefault()
                           ?? throw new FileNotFoundException(fileName);

                return path;
            }

            [NotNull]
            private static Version GetFileVersion([NotNull] string path)
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                return new Version(versionInfo.FileMajorPart, versionInfo.FileMinorPart, versionInfo.FileBuildPart);
            }
        }
    }
}
