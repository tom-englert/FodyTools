namespace FodyTools.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    using FodyTools.Tests.Tools;

    using Mono.Cecil.Cil;

    using Xunit;

    public class InstructionSequencesTests
    {
        private static readonly Instruction _newInstruction = Instruction.Create(OpCodes.Nop);

        [Fact]
        public void SequenceCollectionConstraints()
        {
            var sequences = LoadInstructionSequences();

            Assert.True(((ICollection<InstructionSequence>)sequences).IsReadOnly);

            var sequence5 = sequences[5];

            Assert.False(((ICollection<Instruction>)sequence5).IsReadOnly);
        }

        [Fact]
        public void Sequence5StartsAtInstruction7OnLine213()
        {
            var sequences = LoadInstructionSequences();

            var instructions = sequences.Instructions;
            var sequence5 = sequences[5];

            Assert.Equal(6, sequence5.Count);
            Assert.Equal(instructions[7], sequence5[0]);
            Assert.Equal(OpCodes.Ldloc_0, sequence5[0].OpCode);
            Assert.Equal(218, sequence5.Point?.StartLine);
        }

        [Fact]
        public void InsertInSequence5At1IsEquivalentToInsertInInstructionsAt8()
        {
            var sequences = LoadInstructionSequences();

            var instructions = sequences.Instructions;
            var instructionsBefore = instructions.ToList();
            var sequence5 = sequences[5];

            instructionsBefore.Insert(8, _newInstruction);

            Assert.False(instructionsBefore.SequenceEqual(instructions));

            sequence5.Insert(1, _newInstruction);

            Assert.True(instructionsBefore.SequenceEqual(instructions));
        }

        [Fact]
        public void Sequence5ContainsInstruction8AtIndex1()
        {
            var sequences = LoadInstructionSequences();

            var instructions = sequences.Instructions;
            var sequence5 = sequences[5];

            var instruction8 = instructions[8];
            Assert.Equal(1, sequence5.IndexOf(instruction8));
            Assert.Equal(5, sequences.Select((s, i) => new { Index = i, Contains = s.Contains(instruction8) }).Where(item => item.Contains).Select(item => item.Index).Single());
        }

        [Fact]
        public void RemoveInSequence5At1IsEquivalentToRemoveInInstructionsAt8()
        {
            var sequences = LoadInstructionSequences();

            var instructions = sequences.Instructions;
            var instructionsBefore = instructions.ToList();
            var sequence5 = sequences[5];

            instructionsBefore.RemoveAt(8);

            Assert.False(instructionsBefore.SequenceEqual(instructions));

            sequence5.RemoveAt(1);

            Assert.True(instructionsBefore.SequenceEqual(instructions));
        }

        [Fact]
        public void AddInSequence5IsEquivalentToInsertInInstructionsAt13()
        {
            var sequences = LoadInstructionSequences();

            var instructions = sequences.Instructions;
            var instructionsBefore = instructions.ToList();
            var sequence5 = sequences[5];

            instructionsBefore.Insert(13, _newInstruction);

            Assert.False(instructionsBefore.SequenceEqual(instructions));

            sequence5.Add(_newInstruction);

            Assert.True(instructionsBefore.SequenceEqual(instructions));
        }

        [Fact]
        public void SetInSequence5At1IsEquivalentToSetInInstructionsAt8()
        {
            var sequences = LoadInstructionSequences();

            var instructions = sequences.Instructions;
            var instructionsBefore = instructions.ToList();
            var sequence5 = sequences[5];

            instructionsBefore[8] = _newInstruction;

            Assert.False(instructionsBefore.SequenceEqual(instructions));

            sequence5[1] = _newInstruction;

            Assert.True(instructionsBefore.SequenceEqual(instructions));
        }

        [Fact]
        public void RemoveInSequence5IsEquivalentToRemoveInInstructions()
        {
            var sequences = LoadInstructionSequences();

            var instructions = sequences.Instructions;
            var instructionsBefore = instructions.ToList();
            var sequence5 = sequences[5];

            var instruction = instructionsBefore[8];

            instructionsBefore.Remove(instruction);

            Assert.False(instructionsBefore.SequenceEqual(instructions));

            sequence5.Remove(instruction);

            Assert.True(instructionsBefore.SequenceEqual(instructions));
        }

        [Fact]
        public void CopyOfSequence5StartingAt1IsEquivalentToInstructionsStartingAt7()
        {
            var sequences = LoadInstructionSequences();

            var instructions = sequences.Instructions;
            var instructionsBefore = instructions.ToList();
            var sequence5 = sequences[5];

            var buf = new Instruction[7];

            ((ICollection<Instruction>)sequence5).CopyTo(buf, 1);

            Assert.Null(buf[0]);

            Assert.True(instructionsBefore.Skip(7).Take(6).SequenceEqual(buf.Skip(1)));
        }

        [Fact]
        public void ClearSequenceIsNotImplemented()
        {
            var sequences = LoadInstructionSequences();
            var sequence5 = sequences[5];

            Assert.Throws<NotImplementedException>(() => ((ICollection<Instruction>)sequence5).Clear());
        }

        [Fact]
        public void GetEnumeratorIsValid()
        {
            var sequences = LoadInstructionSequences();

            var sequence5 = sequences[5];

            var enumerator = ((IEnumerable)sequence5).GetEnumerator();

            Assert.True(enumerator.MoveNext());
            Assert.Equal(sequence5[0], enumerator.Current);
        }

        [Fact]
        public void ReturnsOneSequenceIfNoDebugInfoIsPresent()
        {
            var sequences = new InstructionSequences(new Instruction[] { Instruction.Create(OpCodes.Nop)  }, null);

            Assert.Single(sequences);
            Assert.Single(sequences[0]);
        }

        private InstructionSequences LoadInstructionSequences()
        {
            var method = ModuleHelper.LoadMethod(() => new SimpleTestClass().SimpleMethod(default));

            var sequences = new InstructionSequences(method.Body.Instructions, method.DebugInformation.SequencePoints);

            Assert.Equal(12, sequences.Count);

            return sequences;
        }

        private class SimpleTestClass
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
        }
    }
}
