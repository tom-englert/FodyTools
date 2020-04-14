#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace FodyTools.Tests
{
    using System;
    using System.Linq;

    using EmptyAssembly;

    using FodyTools.Tests.Tools;

    using Mono.Cecil.Rocks;

    using Xunit;

    public class ExtensionMethodTests
    {
        [Fact]
        public void OnGenericTypeTest1()
        {
            var module = ModuleHelper.LoadModule<EmptyClass>();
            var importer = new CodeImporter(module);

            var type = importer.Import<SimpleSampleClass>();
            var method = importer.ImportMethod(() => default(SimpleGenericClass<T>)!.Method(default));

            var genericInstanceType = method.DeclaringType.MakeGenericInstanceType(type);

            var genericMethod = method.OnGenericType(genericInstanceType);

            Assert.NotEqual(method, genericMethod);
            Assert.Equal("System.Void FodyTools.SimpleGenericClass`1::Method(System.Func`1<T>)", method.ToString());
            Assert.Equal("System.Void FodyTools.SimpleGenericClass`1<FodyTools.SimpleSampleClass>::Method(System.Func`1<T>)", genericMethod.ToString());
        }

        [Fact]
        public void OnGenericTypeTest2()
        {
            var module = ModuleHelper.LoadModule<EmptyClass>();

            var lazy = typeof(Lazy<>);
            var sourceAssemblyPath = lazy.Assembly.Location;
            var sourceModule = ModuleHelper.LoadModule(sourceAssemblyPath);

            var typeDefinition = sourceModule.GetType(lazy.FullName);
            var type = module.ImportReference(typeDefinition);
            var method = module.ImportReference(typeDefinition.Methods.First(m => m.Parameters.Count == 2 && m.Parameters[1].ParameterType.Name == "Boolean"));

            var genericInstanceType = type.MakeGenericInstanceType(type);

            var genericMethod = method.OnGenericType(genericInstanceType);

            Assert.NotEqual(method, genericMethod);
            Assert.Equal("System.Void System.Lazy`1::.ctor(System.Func`1<T>,System.Boolean)", method.ToString());
            Assert.Equal("System.Void System.Lazy`1<System.Lazy`1>::.ctor(System.Func`1<T>,System.Boolean)", genericMethod.ToString());
        }

        [Fact]
        public void OnGenericTypeTest3()
        {
            var module = ModuleHelper.LoadModule<EmptyClass>();
            var importer = new CodeImporter(module);

            var type = importer.Import<SimpleSampleClass>();
            var method = importer.ImportMethod(() => default(SimpleGenericClass<T>).Method2<T1>(default, default));

            var genericInstanceType = method.DeclaringType.MakeGenericInstanceType(type);

            var genericMethod = method.OnGenericType(genericInstanceType).MakeGenericInstanceMethod(type);

            Assert.Equal("System.Void FodyTools.SimpleGenericClass`1::Method2(System.Func`1<T>,T1)", method.ToString());
            Assert.Equal("System.Void FodyTools.SimpleGenericClass`1<FodyTools.SimpleSampleClass>::Method2<FodyTools.SimpleSampleClass>(System.Func`1<T>,T1)", genericMethod.ToString());
        }

        class T { }
        class T1 { }
    }
}
