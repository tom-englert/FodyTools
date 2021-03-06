﻿#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace FodyTools.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    using Fody;

    using FodyTools.Tests.Tools;

    using Mono.Cecil;

    using Xunit;

    public class SystemReferenceExtensionsMethodsTests
    {
        [Fact]
        public void ImportSystemReferences_Test()
        {
            var assemblyPath = typeof(SimpleSampleClass).GetModuleFileName();
            var weaver = new TestWeaver();
            weaver.ExecuteTestRun(assemblyPath, false, null, null, "ImportExtensionsTests");

            Assert.Equal("System.String", weaver.StringType.FullName);
            Assert.Null(weaver.OptionalType);
            Assert.Equal("System.Boolean System.String::Equals(System.String,System.String,System.StringComparison)", weaver.StringEquals.FullName);
            Assert.Equal("System.Reflection.PropertyInfo System.Type::GetProperty(System.String,System.Reflection.BindingFlags)", weaver.GetPropertyInfo.FullName);
        }

        class TestWeaver : AbstractModuleWeaver
        {
            public MethodReference StringEquals { get; set; }

            public TypeReference StringType { get; set; }

            public TypeReference OptionalType { get; set; }

            public MethodReference GetPropertyInfo { get; set; }

            public override void Execute()
            {
                StringType = this.ImportType<string>();
                #if NETFRAMEWORK
                OptionalType = this.TryImportType<System.Windows.Point>(); // does not exist at all in .netcore
                #endif
                StringEquals = this.ImportMethod(() => string.Equals(default, default, default));
                GetPropertyInfo = this.TryImportMethod(() => default(Type).GetProperty(default, default(BindingFlags)));
            }

            public override IEnumerable<string> GetAssembliesForScanning()
            {
                yield break;
            }
        }
    }
}
