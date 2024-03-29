﻿namespace FodyTools.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    using ICSharpCode.Decompiler;
    using ICSharpCode.Decompiler.Disassembler;
    using ICSharpCode.Decompiler.Metadata;

    using Mono.Cecil;

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
            VerifyTypes(importedTypes, importedModules, targetAssemblyPath, Assert.Equal);
        }

        public static void VerifyTypes(
            IDictionary<TypeDefinition, TypeDefinition> importedTypes,
            ICollection<ModuleDefinition> importedModules,
            string targetAssemblyPath,
            Action<string, string> assert)
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

                var normalizedDecompiledSource = FixSourceNamespaces(assemblyPrefixes, decompiledSource);
                var normalizedDecompiledTarget = FixSystemNamespaces(decompiledTarget);

                File.WriteAllText(Path.Combine(tempPath, "source.txt"), normalizedDecompiledSource);
                File.WriteAllText(Path.Combine(tempPath, "target.txt"), normalizedDecompiledTarget);

                assert(normalizedDecompiledSource, normalizedDecompiledTarget);
            }
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
            private static readonly Regex RvaScrubber = new(@"[ \t]+// Method begins at RVA 0x[0-9A-F]+\r?\n", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex BinaryDataScrubber = new(@"[ \t]+[0-9A-F]{2}( [0-9A-F]{2})+\r?\n", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            private static string Normalize(string value)
            {
                var s1 = RvaScrubber.Replace(value, string.Empty);
                var s2 = BinaryDataScrubber.Replace(s1, string.Empty);
                return s2;
            }

            public static string Decompile(string assemblyPath)
            {
                var output = new PlainTextOutput();
                var disassembler = new ReflectionDisassembler(output, CancellationToken.None) { EntityProcessor = new SortByNameProcessor() };
                using var peFile = new PEFile(assemblyPath);

                disassembler.WriteModuleContents(peFile);

                return Normalize(output.ToString()); ;
            }

            public static string Decompile(string assemblyPath, string className)
            {
                var output = new PlainTextOutput();
                var disassembler = new ReflectionDisassembler(output, CancellationToken.None) { EntityProcessor = new SortByNameProcessor() };
                using var peFile = new PEFile(assemblyPath);

                var type = peFile.Metadata.TypeDefinitions.Single(handle => handle.GetFullTypeName(peFile.Metadata).ToILNameString() == className);

                disassembler.DisassembleType(peFile, type);

                return Normalize(output.ToString()); ;
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
