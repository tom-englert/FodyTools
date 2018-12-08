// ReSharper disable AssignNullToNotNullAttribute

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Mono.Cecil;
using TomsToolbox.Core;
using TomsToolbox.Desktop;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null

namespace FodyTools.Tests
{
    public class CodeImporterTests
    {

#if NETFRAMEWORK
        private const string Framework = "NET";
#else
        private const string Framework = "CORE";
#endif

        [NotNull]
        private readonly ITestOutputHelper _testOutputHelper;

        [NotNull]
        private static string TempPath
        {
            get
            {

                var tempPath = Path.Combine(Path.GetTempPath(), "CodeImporter", Framework);
                Directory.CreateDirectory(tempPath);
                return tempPath;
            }
        }

        public enum AssemblyResolver
        {
            AssemblyModuleResolver,
            LocalModuleResolver
        }

        public CodeImporterTests([NotNull] ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Theory]
        [InlineData(8, AssemblyResolver.AssemblyModuleResolver, typeof(Test<>))]
#if !NETCOREAPP
        [InlineData(7, AssemblyResolver.LocalModuleResolver, typeof(Test<>))]
#endif
        public void SimpleTypesTest(int numberOfTypes, AssemblyResolver assemblyResolver, [NotNull, ItemNotNull] params Type[] types)
        {
            var assemblyPath = Path.Combine(Directories.Target, "EmptyAssembly.dll");
            var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { ReadSymbols = true });

            var governingType = types.First();

            Debug.Assert(module != null, nameof(module) + " != null");
            Debug.Assert(governingType?.Namespace != null, nameof(governingType) + " != null");

            var moduleResolver = assemblyResolver == AssemblyResolver.AssemblyModuleResolver
                ? (IModuleResolver)new AssemblyModuleResolver(typeof(AssemblyExtensions).Assembly, typeof(BinaryOperation).Assembly)
                : new LocalReferenceModuleResolver();

            var targetAssemblyPath = Path.Combine(TempPath, "TargetAssembly1.dll");

            var target = new CodeImporter(module)
            {
                ModuleResolver = moduleResolver,
                HideImportedTypes = false
            };

            foreach (var type in types)
            {
                target.Import(type);
            }

            module.Write(targetAssemblyPath);

            var importedTypes = target.ListImportedTypes();

            foreach (var type in importedTypes)
            {
                _testOutputHelper.WriteLine(type.Key);
            }

            Assert.True(PEVerify.Verify(_testOutputHelper, targetAssemblyPath));
            Assert.Equal(numberOfTypes, importedTypes.Count);

            VerifyTypes(importedTypes, targetAssemblyPath, typeof(Test<>));
        }

        [Theory]
        [InlineData(3, typeof(WeakEventListener<,,>))]
        [InlineData(3, typeof(WeakEventSource<>))]
        [InlineData(13, typeof(WeakEventSource<>), typeof(WeakEventListener<,,>), typeof(Test<>))]
        [InlineData(4, typeof(AutoWeakIndexer<,>))]
        [InlineData(2, typeof(TomsToolbox.Core.CollectionExtensions))]
        public void ComplexTypesTest(int numberOfTypes, [NotNull, ItemNotNull] params Type[] types)
        {
            var assemblyPath = Path.Combine(Directories.Target, "EmptyAssembly.dll");
            var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { ReadSymbols = true });

            var governingType = types.First();

            Debug.Assert(module != null, nameof(module) + " != null");
            Debug.Assert(governingType?.Namespace != null, nameof(governingType) + " != null");

            var target = new CodeImporter(module)
            {
                // else IL comparison will fail:
                HideImportedTypes = false
            };

            foreach (var type in types)
            {
                target.Import(type);
            }

            var tempPath = TempPath;

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly2.dll");

            module.Write(targetAssemblyPath);

            var sourceAssemblyPath = Path.Combine(tempPath, "SourceAssembly2.dll");

            var sourceModule = ModuleDefinition.ReadModule(new Uri(governingType.Assembly.CodeBase).LocalPath);

            sourceModule.Assembly.Name.Name = "SourceAssembly";
            sourceModule.Write(sourceAssemblyPath);

            var importedTypes = target.ListImportedTypes();

            if (importedTypes.ContainsKey("TomsToolbox.Core.NetStandardExtensions"))
            {
                numberOfTypes += 1;
            }

            foreach (var type in importedTypes)
            {
                _testOutputHelper.WriteLine(type.Key);
            }

            Assert.Equal(numberOfTypes, importedTypes.Count);

            VerifyTypes(importedTypes, targetAssemblyPath, typeof(Test<>));

            Assert.True(PEVerify.Verify(_testOutputHelper, targetAssemblyPath));
        }

        [Fact]
        public void ImportMethodTest()
        {
            var assemblyPath = Path.Combine(Directories.Target, "EmptyAssembly.dll");
            var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { ReadSymbols = true });
            var target = new CodeImporter(module);

            var importedMethod1 = target.ImportMethod(() => default(MyEventArgs).GetValue());
            var importedMethod2 = target.ImportMethod(() => default(MyEventArgs).GetValue(default));

            Assert.NotEqual(importedMethod2, importedMethod1);
            Assert.Equal(importedMethod2.DeclaringType, importedMethod1.DeclaringType);
            Assert.Equal(importedMethod2.DeclaringType, target.ListImportedTypes().Single().Value);
            Assert.Empty(importedMethod1.Parameters);
            Assert.Single(importedMethod2.Parameters);
        }

        [Fact]
        public void ImportGenericConstructorTest()
        {
            var assemblyPath = Path.Combine(Directories.Target, "EmptyAssembly.dll");
            var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { ReadSymbols = true });
            var target = new CodeImporter(module);

            var importedMethod1 = target.ImportMethod(() => new ComplexSampleClass<T1, T2>(default, default, default));
            var importedMethod2 = target.ImportMethod(() => default(ComplexSampleClass<T1, T2>).SomeMethod<T>(default, default, default));

            Assert.NotEqual(importedMethod2, importedMethod1);
            Assert.Equal(importedMethod2.DeclaringType, importedMethod1.DeclaringType);
            Assert.Equal(importedMethod2.DeclaringType, target.ListImportedTypes().Single().Value);
            Assert.Equal(3, importedMethod1.Parameters.Count);
            Assert.Equal(3, importedMethod2.Parameters.Count);
            Assert.Equal(".ctor", importedMethod1.Name);
        }

        private class T : TomsToolbox.Core.DelegateComparer<AutoWeakIndexer<int, string>>
        {
            public T() : base(null)
            {
            }
        }

        private class T2 : ITimeService
        {
            public DateTime Now { get; }
            public DateTime Today { get; }
            public DateTime UtcNow { get; }
        }

        private class T1 : DelegateComparer<T2> {
            public T1([NotNull] Func<T2, T2, int> comparer) : base(comparer)
            {
            }
        }


        [Fact]
        public void ImportMethodsThrowsOnInvalidExpression()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module);

            Assert.Throws<ArgumentException>(() => { target.ImportMethod(() => default(MyEventArgs).AnotherValue); });
        }

        [Fact]
        public void ImportPropertyTest()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module);

            var importedProperty = target.ImportProperty(() => default(MyEventArgs).AnotherValue);

            Assert.Equal(importedProperty.DeclaringType, target.ListImportedTypes().Single().Value);
        }

        [Fact]
        public void ImportPropertyThrowsOnInvalidExpression()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module);

            Assert.Throws<ArgumentException>(() => { target.ImportProperty(() => default(MyEventArgs).GetValue()); });
            Assert.Throws<ArgumentException>(() => { target.ImportProperty(() => default(MyEventArgs).field); });
        }

        [Fact]
        public void ImportFieldTest()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module);

            var importedField = target.ImportField(() => default(MyEventArgs).field);

            Assert.Equal(importedField.DeclaringType, target.ListImportedTypes().Single().Value);
        }

        [Fact]
        public void ImportFieldThrowsOnInvalidExpression()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module);

            Assert.Throws<ArgumentException>(() =>
            {
                target.ImportField(() => default(MyEventArgs).GetValue());
            });
            Assert.Throws<ArgumentException>(() =>
            {
                target.ImportField(() => default(MyEventArgs).AnotherValue);
            });
        }

        [Fact]
        public void ILMerge()
        {
            var assemblyPath = Path.Combine(Directories.Target, "DummyAssembly.dll");
            var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { ReadSymbols = true });

            var codeImporter = new CodeImporter(module)
            {
                HideImportedTypes = false,
                ModuleResolver = new AssemblyModuleResolver(typeof(TomsToolbox.Core.AssemblyExtensions).Assembly)
            };

            codeImporter.ILMerge();

            var tempPath = TempPath;

            foreach (var file in new DirectoryInfo(Directories.Target).EnumerateFiles())
            {
                file.CopyTo(Path.Combine(tempPath, file.Name), true);
            }

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly2.dll");

            module.Assembly.Name.Name = "TargetAssembly2";
            var now = DateTime.Now;
            module.Assembly.Name.Version = new Version(now.Year, now.Month, now.Day, (int)now.TimeOfDay.TotalMilliseconds);

            module.Write(targetAssemblyPath);

            var allTypes = module.Types.ToDictionary(t => t.FullName);

            VerifyTypes(allTypes, targetAssemblyPath, typeof(FodyTools.SimpleSampleClass));

            var il = ILDasm.Decompile(targetAssemblyPath);

            File.WriteAllText(Path.ChangeExtension(targetAssemblyPath, ".il"), il);

            Assert.True(PEVerify.Verify(_testOutputHelper, targetAssemblyPath));
        }

        private static void VerifyTypes([NotNull] IDictionary<string, TypeDefinition> importedTypes, [NotNull] string targetAssemblyPath, [NotNull] Type typeInSourceAssembly)
        {
            var sourceAssemblyPath = new Uri(typeInSourceAssembly.Assembly.CodeBase).LocalPath;
            var toolboxCoreAssemblyPath = new Uri(typeof(AssemblyExtensions).Assembly.CodeBase).LocalPath;
            var toolboxDesktopAssemblyPath = new Uri(typeof(BinaryOperation).Assembly.CodeBase).LocalPath;
            var tempPath = TempPath;

            foreach (var t in importedTypes)
            {
                if (t.Key.StartsWith("<"))
                    continue;

                var assemblyPath = t.Key.Contains("FodyTools") ? sourceAssemblyPath : t.Key.Contains("Desktop") ? toolboxDesktopAssemblyPath : toolboxCoreAssemblyPath;
                var decompiled = ILDasm.Decompile(assemblyPath, t.Key);

                var decompiledSource = FixSourceNamespaces(FixIndenting(FixAttributeOrder(decompiled)));
                var decompiledTarget = FixSystemNamespaces(FixIndenting(FixAttributeOrder(ILDasm.Decompile(targetAssemblyPath, t.Value.FullName))));

                File.WriteAllText(Path.Combine(tempPath, "source.txt"), decompiledSource);
                File.WriteAllText(Path.Combine(tempPath, "target.txt"), decompiledTarget);

                var expected = decompiledSource.Replace("\r\n", "\n").Split('\n').OrderBy(line => line);
                var target = decompiledTarget.Replace("\r\n", "\n").Split('\n').OrderBy(line => line);

                var mismatches = Enumerate.AsTuples(expected, target)
                    .Select((tuple, index) => new { tuple.Item1, tuple.Item2, index })
                    .Where(tuple => tuple.Item1 != tuple.Item2)
                    .ToArray();


                Assert.Empty(mismatches);
            }
        }

        [NotNull]
        private static string FixAttributeOrder([NotNull] string value)
        {
            return value.Replace(
"  .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) \r\n  .custom instance void [mscorlib]System.Diagnostics.DebuggerBrowsableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggerBrowsableState) = ( 01 00 00 00 00 00 00 00 ) ",
"  .custom instance void [mscorlib]System.Diagnostics.DebuggerBrowsableAttribute::.ctor(valuetype [mscorlib]System.Diagnostics.DebuggerBrowsableState) = ( 01 00 00 00 00 00 00 00 ) \r\n  .custom instance void [mscorlib]System.Runtime.CompilerServices.CompilerGeneratedAttribute::.ctor() = ( 01 00 00 00 ) ");
        }

        [NotNull]
        static string FixSourceNamespaces([NotNull] string value)
        {
            return FixSystemNamespaces(value.Replace("[TomsToolbox.Core]", "").Replace("[TomsToolbox.Desktop]", ""));
        }

        [NotNull]
        static string FixSystemNamespaces([NotNull] string value)
        {
            var regex = new Regex(@"\[System\.[\.\w]+\]");

            var result = regex.Replace(value, "[System]");

            result = result.Replace("[mscorlib]", "[System]");

            return result;
        }

        [NotNull]
        static string FixIndenting([NotNull] string value)
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

        private static class PEVerify
        {
            private static readonly string _peVerifyPath = SdkTool.Find("PEVerify.exe");

            public static bool Verify(ITestOutputHelper testOutputHelper, string assemblyPath)
            {
                var workingDirectory = Path.GetDirectoryName(assemblyPath);

                var ignoreCodes = new[]
                {
                    "0x80131869", // can't resolve reference => PEVerify can't find the referenced dll...
                    #if NETCOREAPP
                    "0x80070002"  // The system cannot find the file specified.
                    #endif
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
                        testOutputHelper.WriteLine(_peVerifyPath);
                        testOutputHelper.WriteLine(output);

                        return false;
                    }
                }

                return true;
            }
        }

        private static class ILDasm
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
                               .OrderByDescending<string, Version>(GetFileVersion)
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

    // ReSharper disable all (just test code below)

    [Some]
    internal class Test<T> : List<T> where T : EventArgs
    {
        private readonly EventHandler<T> _handler;
        private int _field;
        private readonly EventHandler<T> _delegate;
        private readonly TomsToolbox.Desktop.BinaryOperation _operation;

        public Test(EventHandler<T> handler)
        {
            _handler = handler;
            _field = 0;
            _delegate = OnEvent;
            _operation = BinaryOperation.Division;
        }

        public event EventHandler<T> Tested;

        public int Value()
        {
            return _field;
        }

        public void Add(int value)
        {
            _field += value;
        }

        public void OnEvent(object sender, T e)
        {
            Tested?.Invoke(this, e);
        }

        private Referenced GetReferenced()
        {
            try
            {
                AssemblyExtensions.GeneratePackUri(GetType().Assembly, "Test");
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is AggregateException)
            {
                return null;
            }
            finally
            {
                _field = 0;
            }
        }

        public Referenced Referenced { get; set; }

        public KeyValuePair<string, T> KeyValuePair { get; set; }

        public KeyValuePair<U, T> GetKeyValuePair<U>()
        {
            return default;
        }

        public ITimeService RealTimeService = new RealTimeService();
    }

    internal class MyEventArgs : EventArgs, IEnumerable<string>
    {
        public int field;

        public string GetValue()
        {
            return null;
        }

        public string GetValue(string key)
        {
            return null;
        }

        public IEnumerator<string> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public string AnotherValue { get; set; }
    }

    internal class Referenced : List<string>
    {
#if NETFRAMEWORK
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, int dwFlags);
#endif

        private readonly Test<MyEventArgs> _owner;
        private readonly IntPtr _hlib;

        public Referenced(Test<MyEventArgs> owner)
        {
            _owner = owner;
#if NETFRAMEWORK
            _hlib = LoadLibraryEx("dummy", IntPtr.Zero, 0);
#endif
        }

        public Test<MyEventArgs> Owner
        {
            get
            {
                return _owner;
            }
        }

        public void WithArgs<T>(T value) where T : class, IList
        {

        }
    }

    [AttributeUsage(AttributeTargets.All)]
    internal class SomeAttribute : Attribute
    {

    }
}
