namespace FodyTools.Tests
{
    using System.Linq;
    using System.Runtime.CompilerServices;

    using ApprovalTests;

    using FodyTools.Tests.Tools;

    using Mono.Cecil.Cil;

    using Xunit;

    public class TypeExtensionMethodsTests
    {
        private static readonly Instruction[] _dummyInstructions = {
            Instruction.Create(OpCodes.Ldc_I4, 1),
            Instruction.Create(OpCodes.Ldc_I4, 2),
            Instruction.Create(OpCodes.Add), 
            Instruction.Create(OpCodes.Pop), 
        };

        static TypeExtensionMethodsTests()
        {
            ConfigurationNamer.Register();
        }

        public class SampleWithConstructorBase
        {
            private readonly int _value;

            public SampleWithConstructorBase(int value)
            {
                _value = value;
            }

            public static int SomeMethod(int value)
            {
                return value + 1;
            }
        }

        public class SampleWithConstructors : SampleWithConstructorBase
        {
            public SampleWithConstructors(int value)
                : base(value)
            {
            }

            public SampleWithConstructors()
                : base(SomeMethod(1))
            {
            }

            public SampleWithConstructors(int value1, int value2)
                : this(value1)
            {
            }
        }

        public class SampleWithFinalizer
        {
            public int _i = 3;

            ~SampleWithFinalizer()
            {
                _i = 0;
            }
        }

        public class SampleWithStaticConstructor
        {
            public static readonly int _i = 3;

        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InsertIntoConstructorsTest()
        {
            var type = ModuleHelper.LoadType<SampleWithConstructors>();

            type.InsertIntoConstructors(() => _dummyInstructions);

            Approvals.Verify(type.Decompile());
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InsertIntoFinalizerTest()
        {
            var type = ModuleHelper.LoadType<SampleWithConstructors>();

            type.InsertIntoFinalizer(_dummyInstructions);

            Approvals.Verify(type.Decompile());
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InsertIntoExistingFinalizerTest()
        {
            var type = ModuleHelper.LoadType<SampleWithFinalizer>();

            type.InsertIntoFinalizer(_dummyInstructions);

            Approvals.Verify(type.Decompile());
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InsertIntoStaticConstructorTest()
        {
            var type = ModuleHelper.LoadType<SampleWithConstructors>();

            type.InsertIntoStaticConstructor(_dummyInstructions);

            Approvals.Verify(type.Decompile());
        }

        [Fact]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InsertIntoExistingStaticConstructorTest()
        {
            var type = ModuleHelper.LoadType<SampleWithStaticConstructor>();

            type.InsertIntoStaticConstructor(_dummyInstructions);

            Approvals.Verify(type.Decompile());
        }

        [Fact]
        public void GetDefaultConstructorReturnsNullOnClassWithNoDefaultConstructorTest()
        {
            var type = ModuleHelper.LoadType<SampleWithConstructorBase>();

            var method = type.GetDefaultConstructor();

            Assert.Null(method);
        }

        [Fact]
        public void GetDefaultConstructorReturnsValidConstructorTest()
        {
            var type = ModuleHelper.LoadType<SampleWithConstructors>();

            var method = type.GetDefaultConstructor();

            Assert.NotNull(method);
            Assert.Equal("System.Void FodyTools.Tests.TypeExtensionMethodsTests/SampleWithConstructors::.ctor()", method.FullName);
        }

        [Fact]
        public void GetSelfAndBaseTypesTest()
        {
            var type = ModuleHelper.LoadType<SampleWithConstructors>();

            var expected = new []
            {
                "FodyTools.Tests.TypeExtensionMethodsTests/SampleWithConstructors", 
                "FodyTools.Tests.TypeExtensionMethodsTests/SampleWithConstructorBase",
                "System.Object"
            };
            var result = type.GetSelfAndBaseTypes().Select(t => t.FullName);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetBaseTypesTest()
        {
            var type = ModuleHelper.LoadType<SampleWithConstructors>();

            var expected = new []
            {
                "FodyTools.Tests.TypeExtensionMethodsTests/SampleWithConstructorBase",
                "System.Object"
            };
            var result = type.GetBaseTypes().Select(t => t.FullName);

            Assert.Equal(expected, result);
        }
    }
}
