namespace FodyTools.Tests
{
    using System.Diagnostics;

    using FodyTools.Tests.Tools;

    using Mono.Cecil.Cil;
    using Mono.Cecil.Rocks;

    using Xunit;

    public class MethodEditContextTests
    {
        [Fact]
        public void EditMethodCreatesInvalidDebugInfoWithoutEditContext()
        {
            var method = ModuleHelper.LoadMethod(() => ((SimpleTestClass)null).SimpleMethod(default));

            var methodBody = method.Body;
            var instructions = methodBody.Instructions;

            instructions.Insert(0, Instruction.Create(OpCodes.Nop));
            instructions.Insert(0, Instruction.Create(OpCodes.Nop));

            method.Body.OptimizeMacros();

            Assert.Equal(2, method.DebugInformation.SequencePoints[0].Offset);
            Assert.Equal(2, method.DebugInformation.Scope.Start.Offset);
        }

        [Fact]
        public void ContextFailsWithMessedUpDebugInformation()
        {
            var method = ModuleHelper.LoadMethod(() => ((SimpleTestClass)null).SimpleMethod(default));

            var methodBody = method.Body;
            var instructions = methodBody.Instructions;

            instructions.Insert(0, Instruction.Create(OpCodes.Nop));
            instructions.Insert(0, Instruction.Create(OpCodes.Nop));

            method.Body.OptimizeMacros();

            Assert.Equal(2, method.DebugInformation.SequencePoints[0].Offset);
            Assert.Equal(2, method.DebugInformation.Scope.Start.Offset);

            Assert.Throws<Fody.WeavingException>(() =>
            {
                using (method.CreateEditContext())
                {
                }
            });
        }

        [Fact]
        public void EditMethodCreatesValidDebugInfoWithEditContext()
        {
            var method = ModuleHelper.LoadMethod(() => ((SimpleTestClass)null).SimpleMethod(default));

            using (method.CreateEditContext())
            {
                var methodBody = method.Body;
                var instructions = methodBody.Instructions;

                instructions.Insert(0, Instruction.Create(OpCodes.Nop));
                instructions.Insert(0, Instruction.Create(OpCodes.Nop));
            }

            method.Body.OptimizeMacros();

            Assert.Equal(0, method.DebugInformation.SequencePoints[0].Offset);
            Assert.Equal(0, method.DebugInformation.Scope.Start.Offset);
        }

        [Fact]
        public void EditMethodContextDoesNotFailWithEmptyMethods()
        {
            var method = ModuleHelper.LoadMethod(() => ((SimpleTestClass)null).AbstractMethod());

            using (method.CreateEditContext())
            {
                Assert.False(method.HasBody);
            }
        }

        private abstract class SimpleTestClass
        {
            public void SimpleMethod(int i)
            {
                var m = 0;

                for (var k = 0; k < i; k++)
                {
                    m += k + i;
                }

                Trace.WriteLine(m);
            }

            public abstract void AbstractMethod();
        }
    }
}
