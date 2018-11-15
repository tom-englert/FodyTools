﻿using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Xunit;

namespace FodyTools.Tests
{
    public class ExtensionMethodTests
    {
        [Fact]
        public void OnGenericTypeTest1()
        {
            var assemblyPath = Path.Combine(Directories.Target, "EmptyAssembly.dll");
            var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { ReadSymbols = true });
            var importer = new CodeImporter(module);

            var type = importer.Import<SimpleSampleClass>();
            var method = importer.ImportMethod(() => default(SimpleGenericClass<T>).Method(default));

            var genericInstanceType = method.DeclaringType.MakeGenericInstanceType(type);

            var genericMethod = method.OnGenericType(genericInstanceType);

            Assert.NotEqual(method, genericMethod);
            Assert.Equal("System.Void FodyTools.SimpleGenericClass`1::Method(System.Func`1<T>)", method.ToString());
            Assert.Equal("System.Void FodyTools.SimpleGenericClass`1<FodyTools.SimpleSampleClass>::Method(System.Func`1<T>)", genericMethod.ToString());
        }

        [Fact]
        public void OnGenericTypeTest2()
        {
            var assemblyPath = Path.Combine(Directories.Target, "EmptyAssembly.dll");
            var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { ReadSymbols = true });

            var lazy = typeof(Lazy<>);
            var sourceAssemblyPath = lazy.Assembly.Location;
            var sourceModule = ModuleDefinition.ReadModule(sourceAssemblyPath, new ReaderParameters { ReadSymbols = false });

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
            var assemblyPath = Path.Combine(Directories.Target, "EmptyAssembly.dll");
            var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { ReadSymbols = true });
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
