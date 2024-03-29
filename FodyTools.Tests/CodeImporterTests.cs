﻿// ReSharper disable all
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
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using EmptyAssembly;

    using FodyTools.Tests.Tools;

    using ICSharpCode.Decompiler;
    using ICSharpCode.Decompiler.Disassembler;
    using ICSharpCode.Decompiler.Metadata;

    using Mono.Cecil;

    using ReferencedAssembly;

    using VerifyXunit;

    using Xunit;
    using Xunit.Abstractions;

    [UsesVerify]
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
                ? (IModuleResolver)new AssemblyModuleResolver(typeof(TomsToolbox.Essentials.AssemblyExtensions).Assembly, typeof(TomsToolbox.Essentials.DefaultValue).Assembly)
                : new LocalReferenceModuleResolver();

            var targetAssemblyPath = Path.Combine(TestHelper.TempPath, "TargetAssembly1.dll");

            var target = new CodeImporter(module)
            {
                ModuleResolver = moduleResolverInstance,
                HideImportedTypes = false,
                CompactMode = false
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
        [InlineData(2, typeof(TomsToolbox.Essentials.WeakEventListener<,,>))]
        [InlineData(3, typeof(TomsToolbox.Essentials.WeakEventSource<>))]
        [InlineData(12, typeof(TomsToolbox.Essentials.WeakEventSource<>), typeof(TomsToolbox.Essentials.WeakEventListener<,,>), typeof(Test<>))]
        [InlineData(4, typeof(TomsToolbox.Essentials.AutoWeakIndexer<,>))]
        [InlineData(2, typeof(TomsToolbox.Essentials.CollectionExtensions))]
        public void ComplexTypesTest(int numberOfTypes, params Type[] types)
        {
            var module = ModuleHelper.LoadModule<EmptyClass>();

            var governingType = types.First();

            var target = new CodeImporter(module)
            {
                // else IL comparison will fail:
                HideImportedTypes = false,
                CompactMode = false
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

            if (importedTypes.Keys.Select(t => t.FullName).Contains("TomsToolbox.Essentials.NetStandardExtensions"))
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

            var target = new CodeImporter(module) { CompactMode = false };

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

            var target = new CodeImporter(module) { CompactMode = false };

            var importedMethod1 = target.ImportMethod(() => new ComplexSampleClass<T1, T2>(default!, default!, default));
            var importedMethod2 = target.ImportMethod(() => default(ComplexSampleClass<T1, T2>)!.SomeMethod<T>(default, default, default));

            Assert.NotEqual(importedMethod2, importedMethod1);
            Assert.Equal(importedMethod2.DeclaringType, importedMethod1.DeclaringType);
            Assert.Equal(importedMethod2.DeclaringType, target.ListImportedTypes().Single().Value);
            Assert.Equal(3, importedMethod1.Parameters.Count);
            Assert.Equal(3, importedMethod2.Parameters.Count);
            Assert.Equal(".ctor", importedMethod1.Name);
        }

        private class T : TomsToolbox.Essentials.DelegateComparer<TomsToolbox.Essentials.AutoWeakIndexer<int, string>>
        {
            public T() : base(null)
            {
            }
        }

        private class T2 : TomsToolbox.Essentials.ITimeService
        {
            public DateTime Now { get; }
            public DateTime Today { get; }
            public DateTime UtcNow { get; }
        }

        private class T1 : TomsToolbox.Essentials.DelegateComparer<T2>
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
            var target = new CodeImporter(module) { CompactMode = false };

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
                ModuleResolver = new AssemblyModuleResolver(typeof(TomsToolbox.Essentials.AssemblyExtensions).Assembly, typeof(Structure).Assembly),
                NamespaceDecorator = ns => namespacePrefix + ns,
                CompactMode = false
            };

            codeImporter.ILMerge();

            var tempPath = TestHelper.TempPath;

            CopyLocalReferences(module, tempPath);

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

            Assert.True(TestHelper.PEVerify.Verify(targetAssemblyPath, _testOutputHelper, "0x80131252", "0x80131869"));
        }

        [Fact]
        public void ILMergeNullable()
        {
            var module = ModuleHelper.LoadModule<DummyNullableAssembly.SimpleSampleClass>();

            var codeImporter = new CodeImporter(module)
            {
                HideImportedTypes = false,
                ModuleResolver = new AssemblyModuleResolver(typeof(TomsToolbox.Essentials.AssemblyExtensions).Assembly, typeof(Structure).Assembly),
                CompactMode = false
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

            Assert.True(TestHelper.PEVerify.Verify(targetAssemblyPath, _testOutputHelper, "0x80131252", "0x80131869"));
        }

#if NETFRAMEWORK
        [Fact]
        public async void ILMerge2()
        {
            var module = ModuleHelper.LoadModule<ShellAssembly.Program>();

            static bool CanDeferMethodImport(MethodDefinition method)
            {
                if (method.IsConstructor)
                    return false;

                if (method.IsStatic)
                    return true;

                if (method.HasOverrides || method.IsAbstract || method.IsVirtual || method.IsPInvokeImpl)
                    return false;

                return true;
            }

            var codeImporter = new CodeImporter(module)
            {
                HideImportedTypes = false,
                ModuleResolver = new AssemblyModuleResolver(
                    typeof(Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialog).Assembly,
                    typeof(Microsoft.WindowsAPICodePack.Dialogs.TaskDialog).Assembly,
                    typeof(Newtonsoft.Json.JsonConvert).Assembly),
                CompactMode = true
            };

            codeImporter.ILMerge();

            var tempPath = TestHelper.TempPath;

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly2.dll");

            module.Assembly.Name.Name = "TargetAssembly2";
            module.Write(targetAssemblyPath);

            Assert.True(TestHelper.PEVerify.Verify(targetAssemblyPath, _testOutputHelper));

            //var il = TestHelper.ILDasm.Decompile(targetAssemblyPath).RemoveComments();

            //await Verifier.Verify(il).UniqueForRuntime().UniqueForAssemblyConfiguration();

            await Task.CompletedTask;
        }
#endif

        [Fact]
        public async void CompactModeTest()
        {
            var module = ModuleHelper.LoadModule<SimpleSampleClass>();

            var codeImporter = new CodeImporter(module)
            {
                ModuleResolver = new AssemblyModuleResolver(typeof(Structure).Assembly),
            };

            codeImporter.ILMerge();

            var tempPath = TestHelper.TempPath;

            CopyLocalReferences(module, tempPath);

            var targetAssemblyPath = Path.Combine(tempPath, "TargetAssembly2.dll");

            module.Assembly.Name.Name = "TargetAssembly2";
            module.Write(targetAssemblyPath);

            var il = TestHelper.ILDasm.Decompile(targetAssemblyPath);

            await Verify(il).UniqueForRuntime().UniqueForAssemblyConfiguration();

            Assert.True(TestHelper.PEVerify.Verify(targetAssemblyPath, _testOutputHelper, "0x80131252", "0x80131869"));
        }

        private static void CopyLocalReferences(ModuleDefinition module, string targetPath)
        {
            foreach (var file in new DirectoryInfo(Path.GetDirectoryName(module.FileName)).EnumerateFiles())
            {
                file.CopyTo(Path.Combine(targetPath, file.Name), true);
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
                TomsToolbox.Essentials.AssemblyExtensions.GeneratePackUri(GetType().Assembly, "Test");
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

        public TomsToolbox.Essentials.ITimeService RealTimeService = new TomsToolbox.Essentials.RealTimeService();
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
