﻿namespace FodyTools.Tests
{
    using System;
    using System.Text;

    using ApprovalTests;

    using FodyTools.Tests.Tools;

    using Mono.Cecil.Cil;

    using Xunit;

    public class InsertIntoConstructorsTests
    {
        static InsertIntoConstructorsTests()
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

        [Fact]
        public void InsertIntoConstructorsTest()
        {
            var type = ModuleHelper.LoadType<SampleWithConstructors>();

            type.InsertIntoConstructors(() => new []
            {
                Instruction.Create(OpCodes.Ldc_I4, 1),
                Instruction.Create(OpCodes.Ldc_I4, 2),
                Instruction.Create(OpCodes.Add), 
                Instruction.Create(OpCodes.Pop), 
            });

            Approvals.Verify(type.Decompile());
        }
    }
}
