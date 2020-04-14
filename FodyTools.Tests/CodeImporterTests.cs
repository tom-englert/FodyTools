// ReSharper disable all
#pragma warning disable 649
#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.

#nullable disable

namespace FodyTools.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    using EmptyAssembly;

    using FodyTools.Tests.Tools;

    using Mono.Cecil;

    using ReferencedAssembly;

    using Xunit;
    using Xunit.Abstractions;

    public class CodeImporterTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public enum ModuleResolver
        {
            AssemblyModuleResolver,
            LocalModuleResolver
        }

        public CodeImporterTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Theory]
#if NETCOREAPP
        [InlineData(7, ModuleResolver.AssemblyModuleResolver, typeof(Test<>))]
#else
        [InlineData(7, ModuleResolver.LocalModuleResolver, typeof(Test<>))]
#endif
        public void SimpleTypesTest(int numberOfTypes, ModuleResolver moduleResolver, params Type[] types)
        {
            var module = ModuleHelper.LoadModule<EmptyClass>();

            var moduleResolverInstance = moduleResolver == ModuleResolver.AssemblyModuleResolver
                ? (IModuleResolver)new AssemblyModuleResolver(typeof(TomsToolbox.Core.AssemblyExtensions).Assembly, typeof(TomsToolbox.Core.DefaultValue).Assembly)
                : new LocalReferenceModuleResolver();

            var targetAssemblyPath = Path.Combine(TestHelper.TempPath, "TargetAssembly1.dll");

            var target = new CodeImporter(module)
            {
                ModuleResolver = moduleResolverInstance,
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
                _testOutputHelper.WriteLine(type.Key.FullName);
            }

            var importedModules = target.ListImportedModules();

            Assert.True(TestHelper.PEVerify.Verify(targetAssemblyPath, _testOutputHelper));
            Assert.Equal(numberOfTypes, importedTypes.Count);

            TestHelper.VerifyTypes(importedTypes, importedModules, targetAssemblyPath);
        }

        [Theory]
        [InlineData(3, typeof(TomsToolbox.Core.WeakEventListener<,,>))]
        [InlineData(3, typeof(TomsToolbox.Core.WeakEventSource<>))]
        [InlineData(13, typeof(TomsToolbox.Core.WeakEventSource<>), typeof(TomsToolbox.Core.WeakEventListener<,,>), typeof(Test<>))]
        [InlineData(4, typeof(TomsToolbox.Core.AutoWeakIndexer<,>))]
        [InlineData(2, typeof(TomsToolbox.Core.CollectionExtensions))]
        public void ComplexTypesTest(int numberOfTypes, params Type[] types)
        {
            var module = ModuleHelper.LoadModule<EmptyClass>();

            var governingType = types.First();

            var target = new CodeImporter(module)
            {
                // else IL comparison will fail:
                HideImportedTypes = false,
            };

            foreach (var type in types)
            {
                target.Import(type);
            }

            var tempPath = TestHelper.TempPath;

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly2.dll");

            module.Write(targetAssemblyPath);

            var sourceAssemblyPath = Path.Combine(tempPath, "SourceAssembly2.dll");

            var sourceModule = ModuleHelper.LoadModule(new Uri(governingType.Assembly.CodeBase).LocalPath);

            sourceModule.Assembly.Name.Name = "SourceAssembly";
            sourceModule.Write(sourceAssemblyPath);

            var importedTypes = target.ListImportedTypes();

            if (importedTypes.Keys.Select(t => t.FullName).Contains("TomsToolbox.Core.NetStandardExtensions"))
            {
                numberOfTypes += 1;
            }

            foreach (var type in importedTypes)
            {
                _testOutputHelper.WriteLine(type.Key.FullName);
            }


            Assert.Equal(numberOfTypes, importedTypes.Count);

            TestHelper.VerifyTypes(importedTypes, target.ListImportedModules(), targetAssemblyPath);

            Assert.True(TestHelper.PEVerify.Verify(targetAssemblyPath, _testOutputHelper));
        }

        [Fact]
        public void ImportMethodTest()
        {
            var module = ModuleHelper.LoadModule<EmptyClass>();

            var target = new CodeImporter(module);

            var importedMethod1 = target.ImportMethod(() => default(MyEventArgs)!.GetValue());
            var importedMethod2 = target.ImportMethod(() => default(MyEventArgs)!.GetValue(default!));

            Assert.NotEqual(importedMethod2, importedMethod1);
            Assert.Equal(importedMethod2.DeclaringType, importedMethod1.DeclaringType);
            Assert.Equal(importedMethod2.DeclaringType, target.ListImportedTypes().Single().Value);
            Assert.Empty(importedMethod1.Parameters);
            Assert.Single(importedMethod2.Parameters);
        }

        [Fact]
        public void ImportGenericConstructorTest()
        {
            var module = ModuleHelper.LoadModule<EmptyClass>();

            var target = new CodeImporter(module);

            var importedMethod1 = target.ImportMethod(() => new ComplexSampleClass<T1, T2>(default!, default!, default));
            var importedMethod2 = target.ImportMethod(() => default(ComplexSampleClass<T1, T2>)!.SomeMethod<T>(default, default, default));

            Assert.NotEqual(importedMethod2, importedMethod1);
            Assert.Equal(importedMethod2.DeclaringType, importedMethod1.DeclaringType);
            Assert.Equal(importedMethod2.DeclaringType, target.ListImportedTypes().Single().Value);
            Assert.Equal(3, importedMethod1.Parameters.Count);
            Assert.Equal(3, importedMethod2.Parameters.Count);
            Assert.Equal(".ctor", importedMethod1.Name);
        }

        private class T : TomsToolbox.Core.DelegateComparer<TomsToolbox.Core.AutoWeakIndexer<int, string>>
        {
            public T() : base(null)
            {
            }
        }

        private class T2 : TomsToolbox.Core.ITimeService
        {
            public DateTime Now { get; }
            public DateTime Today { get; }
            public DateTime UtcNow { get; }
        }

        private class T1 : TomsToolbox.Core.DelegateComparer<T2>
        {
            public T1(Func<T2, T2, int> comparer) : base(comparer)
            {
            }
        }


        [Fact]
        public void ImportMethodsThrowsOnInvalidExpression()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = ModuleHelper.AssemblyResolver });
            var target = new CodeImporter(module);

            Assert.Throws<ArgumentException>(() => { target.ImportMethod(() => default(MyEventArgs).AnotherValue); });
        }

        [Fact]
        public void ImportPropertyTest()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = ModuleHelper.AssemblyResolver });
            var target = new CodeImporter(module);

            var importedProperty = target.ImportProperty(() => default(MyEventArgs).AnotherValue);

            Assert.Equal(importedProperty.DeclaringType, target.ListImportedTypes().Single().Value);
        }

        [Fact]
        public void ImportPropertyThrowsOnInvalidExpression()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = ModuleHelper.AssemblyResolver });
            var target = new CodeImporter(module);

            Assert.Throws<ArgumentException>(() => { target.ImportProperty(() => default(MyEventArgs).GetValue()); });
            Assert.Throws<ArgumentException>(() => { target.ImportProperty(() => default(MyEventArgs).field); });
        }

        [Fact]
        public void ImportFieldTest()
        {
            var module = ModuleDefinition.CreateModule("CodeImporterSmokeTest", new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = ModuleHelper.AssemblyResolver });
            var target = new CodeImporter(module);

            var importedField = target.ImportField(() => default(MyEventArgs).field);
            var importedTypes = target.ListImportedTypes();

            Assert.Equal(importedField.DeclaringType, importedTypes.Single().Value);
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


        [Theory]
        [InlineData("")]
        [InlineData("$$$")]
        public void ILMerge(string namespacePrefix)
        {
            var module = ModuleHelper.LoadModule<SimpleSampleClass>();

            var codeImporter = new CodeImporter(module)
            {
                HideImportedTypes = false,
                ModuleResolver = new AssemblyModuleResolver(typeof(TomsToolbox.Core.AssemblyExtensions).Assembly, typeof(Structure).Assembly),
                NamespaceDecorator = ns => namespacePrefix + ns,
            };

            codeImporter.ILMerge();

            var tempPath = TestHelper.TempPath;

            foreach (var file in new DirectoryInfo(Path.GetDirectoryName(module.FileName)).EnumerateFiles())
            {
                file.CopyTo(Path.Combine(tempPath, file.Name), true);
            }

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly2.dll");

            module.Assembly.Name.Name = "TargetAssembly2";
            var now = DateTime.Now;
            module.Assembly.Name.Version = new Version(now.Year, now.Month, now.Day, (int)now.TimeOfDay.TotalMilliseconds);

            module.Write(targetAssemblyPath);

            var importedModules = codeImporter.ListImportedModules();
            var importedTypes = codeImporter.ListImportedTypes();

            TestHelper.VerifyTypes(importedTypes, importedModules, targetAssemblyPath);

            var il = TestHelper.ILDasm.Decompile(targetAssemblyPath);

            File.WriteAllText(Path.ChangeExtension(targetAssemblyPath, ".il"), il);

            // TODO: check why we get this when target is NetCore
            // [MD](0x80131252): Error: Token 0x0200001e following ELEMENT_TYPE_CLASS (_VALUETYPE) in signature is a ValueType (Class,respectively). [token:0x04000004]

            Assert.True(TestHelper.PEVerify.Verify(targetAssemblyPath, _testOutputHelper, "0x80131252"));
        }

        [Fact]
        public void ILMerge2()
        {
            var targetDir = Path.Combine(GetType().GetModuleFolder(), "Binaries");

            using (new CurrentDirectory(targetDir))
            {
                var assemblyPath = Path.Combine(targetDir, "ResxManager.exe");
                var module = ModuleHelper.LoadModule(assemblyPath);

                var codeImporter = new CodeImporter(module)
                {
                    HideImportedTypes = false,
                    ModuleResolver = new LocalReferenceModuleResolver()
                };

                codeImporter.ILMerge();

                var tempPath = TestHelper.TempPath;

                foreach (var file in new DirectoryInfo(targetDir).EnumerateFiles())
                {
                    file.CopyTo(Path.Combine(tempPath, file.Name), true);
                }

                var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly2.dll");

                module.Assembly.Name.Name = "TargetAssembly2";
                var now = DateTime.Now;
                module.Assembly.Name.Version = new Version(now.Year, now.Month, now.Day, (int)now.TimeOfDay.TotalMilliseconds);

                module.Write(targetAssemblyPath);

                Assert.True(TestHelper.PEVerify.Verify(targetAssemblyPath, _testOutputHelper));
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
        // private readonly TomsToolbox.Desktop.BinaryOperation _operation;

        public Test(EventHandler<T> handler)
        {
            _handler = handler;
            _field = 0;
            _delegate = OnEvent;
            // _operation = TomsToolbox.Desktop.BinaryOperation.Division;
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
                TomsToolbox.Core.AssemblyExtensions.GeneratePackUri(GetType().Assembly, "Test");
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

        public TomsToolbox.Core.ITimeService RealTimeService = new TomsToolbox.Core.RealTimeService();
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
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, int dwFlags);
#endif

        private readonly Test<MyEventArgs> _owner;
#if NETFRAMEWORK
        private readonly IntPtr _hlib;
#endif
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

    internal sealed class CurrentDirectory : IDisposable
    {
        private readonly string _currendDir = Directory.GetCurrentDirectory();

        public CurrentDirectory(string directoryName)
        {
            Directory.SetCurrentDirectory(directoryName);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_currendDir);
        }
    }
}
