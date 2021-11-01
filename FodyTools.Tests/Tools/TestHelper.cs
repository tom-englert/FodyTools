namespace FodyTools.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    using Mono.Cecil;

    using TomsToolbox.Core;

    using Xunit;
    using Xunit.Abstractions;

    public static class TestHelper
    {
#if NETFRAMEWORK
        private const string Framework = "NET";
#else
        private const string Framework = "CORE";
#endif

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
            IDictionary<TypeDefinition, TypeDefinition> importedTypes,
            ICollection<ModuleDefinition> importedModules,
            string targetAssemblyPath)
        {
            VerifyTypes(importedTypes, importedModules, targetAssemblyPath, (assemblyName, source, target) => AssertIlStrict(source, target));
        }

        public static void VerifyTypes(
            IDictionary<TypeDefinition, TypeDefinition> importedTypes,
            ICollection<ModuleDefinition> importedModules,
            string targetAssemblyPath,
            Action<string, string, string> assert)
        {
            var assemblyPrefixes = importedModules
                .Select(m => $"[{m.Assembly.Name.Name}]")
                .ToList()
                .AsReadOnly();

            var tempPath = TempPath;

            foreach (var type in importedTypes)
            {
                var sourceType = type.Key;
                var sourceTypeName = sourceType.FullName;
                var targetType = type.Value;
                var targetTypeName = targetType.FullName;
                if (sourceTypeName.StartsWith("<"))
                    continue;

                var assemblyPath = sourceType.Module.FileName;

                var prefix = targetTypeName.Substring(0, targetTypeName.Length - sourceTypeName.Length);

                var sourceModuleName = sourceType.Module.Name;

                var decompiledSource = ILDasm.Decompile(assemblyPath, sourceTypeName);
                var decompiledTarget = ILDasm.Decompile(targetAssemblyPath, targetTypeName).Replace($"{sourceModuleName}!", "");

                if (!string.IsNullOrEmpty(prefix))
                {
                    decompiledTarget = decompiledTarget
                        .Replace(prefix + ".", string.Empty)
                        .Replace(prefix, string.Empty);
                }

                var normalizedDecompiledSource = FixSourceNamespaces(assemblyPrefixes, FixIndenting(FixAttributeOrder(decompiledSource)));
                var normalizedDecompiledTarget = FixSystemNamespaces(FixIndenting(FixAttributeOrder(decompiledTarget)));

                File.WriteAllText(Path.Combine(tempPath, "source.txt"), normalizedDecompiledSource);
                File.WriteAllText(Path.Combine(tempPath, "target.txt"), normalizedDecompiledTarget);

                assert(targetTypeName, normalizedDecompiledSource, normalizedDecompiledTarget);
            }
        }

        private static readonly Regex _binaryDataRegex = new Regex("^[0-9A-F]{2} [0-9A-F]{2}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsNotBinaryData(string line)
        {
            if (line.Contains("Attribute::.ctor(class [System]System.Type)"))
                return false;

            if (_binaryDataRegex.Match(line).Success)
                return false;

            return true;
        }

        public static void AssertIlStrict(string decompiledSource, string decompiledTarget)
        {
            var expected = decompiledSource.Replace("\r\n", "\n").Split('\n').OrderBy(line => line).SkipWhile(string.IsNullOrWhiteSpace).Where(IsNotBinaryData); 
            var target = decompiledTarget.Replace("\r\n", "\n").Split('\n').OrderBy(line => line).SkipWhile(string.IsNullOrWhiteSpace).Where(IsNotBinaryData);

            var mismatches = Enumerate.AsTuples(expected, target)
                .Select((tuple, index) => new { tuple.Item1, tuple.Item2, index })
                .Where(tuple => tuple.Item1 != tuple.Item2)
                .ToList();

            Assert.Empty(mismatches);
        }

        public static string RemoveComments(this string il)
        {
            return string.Join("\r\n", il.Replace("\r\n", "\n").Split('\n').Where(IsData));
        }

        private static bool IsData(string value)
        {
            return !value.TrimStart().StartsWith("//");
        }

        private static string FixAttributeOrder(string value)
        {
            return value.Replace(
                "  .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) \r\n  .custom instance void [mscorlib]System.Diagnostics.DebuggerBrowsableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggerBrowsableState) = ( 01 00 00 00 00 00 00 00 ) ",
                "  .custom instance void [mscorlib]System.Diagnostics.DebuggerBrowsableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggerBrowsableState) = ( 01 00 00 00 00 00 00 00 ) \r\n  .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) ");
        }

        private static string FixSourceNamespaces(IEnumerable<string> assemblyPrefixes, string value)
        {
            value = assemblyPrefixes.Aggregate(value, (current, modulePrefix) => current.Replace(modulePrefix, string.Empty));

            return FixSystemNamespaces(value);
        }

        private static string FixSystemNamespaces(string value)
        {
            var regex = new Regex(@"\[System\.[\.\w]+\]");

            var result = regex.Replace(value, "[System]");

            result = result.Replace("[netstandard]", "[System]");
            result = result.Replace("[mscorlib]", "[System]");
            result = result.Replace("[WindowsBase]", "[System]");

            return result;
        }

        private static string FixIndenting(string value)
        {
            return string.Join(Environment.NewLine, value.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Select(TrimIndent));
        }

        private static string TrimIndent(string line)
        {
            var spaces = new string(' ', 16);
            if (line.StartsWith(spaces))
                return line.TrimStart(' ');

            return line;
        }

        public static class PEVerify
        {
            private static readonly string _peVerifyPath = SdkTool.Find("PEVerify.exe");

            public static bool Verify(string assemblyPath, ITestOutputHelper testOutputHelper, params string[] additionalIgnoreCodes)
            {
                return Verify(assemblyPath, testOutputHelper.WriteLine, additionalIgnoreCodes);
            }

            public static bool Verify(string assemblyPath, Action<string> writeOutput, params string[] additionalIgnoreCodes)
            {
                var workingDirectory = Path.GetDirectoryName(assemblyPath);

                var ignoreCodes = new[]
                {
                    "0x80131869", // can't resolve reference => PEVerify can't find the referenced dll...
                    "0x80070002", // The system cannot find the file specified.
                    "0x801318F3"  // Type load failed 
                }.Concat(additionalIgnoreCodes);

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
            public static string Find(string fileName)
            {
                var windowsSdkDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft SDKs\\Windows");


                var path = Directory.EnumerateFiles(windowsSdkDirectory, fileName, SearchOption.AllDirectories)
                               .Where(item => item?.IndexOf("x64", StringComparison.OrdinalIgnoreCase) == -1)
                               .OrderByDescending(GetFileVersion)
                               .FirstOrDefault()
                           ?? throw new FileNotFoundException(fileName);

                return path;
            }

            private static Version GetFileVersion(string path)
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                return new Version(versionInfo.FileMajorPart, versionInfo.FileMinorPart, versionInfo.FileBuildPart);
            }
        }
    }
}
