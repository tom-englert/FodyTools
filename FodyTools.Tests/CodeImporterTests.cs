#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null

namespace FodyTools.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;

    using FodyTools;

    using JetBrains.Annotations;

    using Mono.Cecil;

    using TomsToolbox.Core;

    using Xunit;

    public class CodeImporterTests
    {
        [Theory]
        [InlineData(4, typeof(Test<>))]
        public void SimpleTypesTest(int numberOfTypes, [NotNull, ItemNotNull] params Type[] types)
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);

            var governingType = types.First();

            Debug.Assert(module != null, nameof(module) + " != null");
            Debug.Assert(governingType?.Namespace != null, nameof(governingType) + " != null");

            var target = new CodeImporter(module, governingType.Namespace);

            var sourceAssemblyPath = governingType.Assembly.Location;

            foreach (var type in types)
            {
                target.Import(type);
            }

            var tempPath = Path.GetTempPath();

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly1.dll");

            module.Write(targetAssemblyPath);

            var importedTypes = target.ListImportedTypes();

            Assert.Equal(numberOfTypes, importedTypes.Count);

            foreach (var t in importedTypes)
            {
                var decompiledSource = ILDasm.Decompile(sourceAssemblyPath, t.FullName);
                var decompiledTarget = ILDasm.Decompile(targetAssemblyPath, t.FullName);

                File.WriteAllText(Path.Combine(tempPath, "source.txt"), decompiledSource);
                File.WriteAllText(Path.Combine(tempPath, "target.txt"), decompiledTarget);

                Assert.Equal(decompiledSource, decompiledTarget);
            }
        }

        [Theory]
        [InlineData(3, typeof(WeakEventListener<,,>))]
        [InlineData(1, typeof(WeakEventSource<>))]
        [InlineData(8, typeof(WeakEventSource<>), typeof(WeakEventListener<,,>), typeof(Test<>))]
        public void ComplexTypesTest(int numberOfTypes, [NotNull, ItemNotNull] params Type[] types)
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);

            var governingType = types.First();

            Debug.Assert(module != null, nameof(module) + " != null");
            Debug.Assert(governingType?.Namespace != null, nameof(governingType) + " != null");

            var target = new CodeImporter(module, governingType.Namespace);

            foreach (var type in types)
            {
                target.RegisterSourceModule(type.Assembly, readSymbols: false);
            }

            var sourceAssemblyPath = governingType.Assembly.Location;

            foreach (var type in types)
            {
                target.Import(type);
            }

            var tempPath = Path.GetTempPath();

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly2.dll");

            module.Write(targetAssemblyPath);

            var importedTypes = target.ListImportedTypes();

            Assert.Equal(numberOfTypes, importedTypes.Count);

            // TODO: Does not work for complex types with dependencies, order of methods will be different...
            /*
            foreach (var t in importedTypes)
            {
                var decompiledSource = ILDasm.Decompile(sourceAssemblyPath, t.FullName);
                var decompiledTarget = ILDasm.Decompile(targetAssemblyPath, t.FullName);

                File.WriteAllText(Path.Combine(tempPath, "source.txt"), decompiledSource);
                File.WriteAllText(Path.Combine(tempPath, "target.txt"), decompiledTarget);

                Assert.Equal(decompiledSource, decompiledTarget);
            }
            */
        }

        [Fact]
        public void ImportMethodTest()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module, "Test");

            var importedMethod1 = target.ImportMethod(() => default(MyEventArgs).GetValue());
            var importedMethod2 = target.ImportMethod(() => default(MyEventArgs).GetValue(default));

            Assert.NotEqual(importedMethod2, importedMethod1);
            Assert.Equal(importedMethod2.DeclaringType, importedMethod1.DeclaringType);
            Assert.Equal(importedMethod2.DeclaringType, target.ListImportedTypes().Single());
            Assert.Empty(importedMethod1.Parameters);
            Assert.Single(importedMethod2.Parameters);
        }

        [Fact]
        public void ImportMethodsThrowsOnInvalidExpression()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module, "Test");

            Assert.Throws<ArgumentException>(() =>
            {
                target.ImportMethod(() => default(MyEventArgs).AnotherValue);
            });
        }

        [Fact]
        public void ImportPropertyTest()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module, "Test");

            var importedProperty = target.ImportProperty(() => default(MyEventArgs).AnotherValue);

            Assert.Equal(importedProperty.DeclaringType, target.ListImportedTypes().Single());
        }

        [Fact]
        public void ImportPropertyThrowsOnInvalidExpression()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module, "Test");

            Assert.Throws<ArgumentException>(() =>
            {
                target.ImportProperty(() => default(MyEventArgs).GetValue());
            });
            Assert.Throws<ArgumentException>(() =>
            {
                target.ImportProperty(() => default(MyEventArgs).field);
            });
        }

        [Fact]
        public void ImportFieldTest()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module, "Test");

            var importedField = target.ImportField(() => default(MyEventArgs).field);

            Assert.Equal(importedField.DeclaringType, target.ListImportedTypes().Single());
        }

        [Fact]
        public void ImportFieldThrowsOnInvalidExpression()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", ModuleKind.Dll);
            var target = new CodeImporter(module, "Test");

            Assert.Throws<ArgumentException>(() =>
            {
                target.ImportField(() => default(MyEventArgs).GetValue());
            });
            Assert.Throws<ArgumentException>(() =>
            {
                target.ImportField(() => default(MyEventArgs).AnotherValue);
            });
        }

        private static class ILDasm
        {
            private static readonly string _ilDasmPath = FindILDasm();

            [NotNull]
            public static string Decompile(string assemblyPath, string className)
            {
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

            [NotNull]
            private static string FindILDasm()
            {
                var windowsSdkDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft SDKs\\Windows");
                var path = Directory.EnumerateFiles(windowsSdkDirectory, "ILDasm.exe", SearchOption.AllDirectories)
                    .Where(item => item?.IndexOf("x64", StringComparison.OrdinalIgnoreCase) == -1)
                    .OrderByDescending(GetFileVersion)
                    .FirstOrDefault()
                    ?? throw new FileNotFoundException("ILDasm.exe");

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

        public Test(EventHandler<T> handler)
        {
            _handler = handler;
            _field = 0;
            _delegate = OnEvent;
        }

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

        }

        private Referenced GetReferenced()
        {
            try
            {
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
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, int dwFlags);

        private readonly Test<MyEventArgs> _owner;
        private readonly IntPtr _hlib;

        public Referenced(Test<MyEventArgs> owner)
        {
            _owner = owner;
            _hlib = LoadLibraryEx("dummy", IntPtr.Zero, 0);
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
