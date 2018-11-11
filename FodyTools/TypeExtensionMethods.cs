namespace FodyTools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using JetBrains.Annotations;

    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using Mono.Cecil.Rocks;

    internal static class TypeExtensionMethods
    {
        /// <summary>
        /// Gets the default constructor of a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The <see cref="MethodDefinition"/> of the default constructor.</returns>
        [CanBeNull]
        public static MethodDefinition GetDefaultConstructor([NotNull] this TypeDefinition type)
        {
            return type.GetConstructors().FirstOrDefault(ctor => ctor.HasBody && ctor.Parameters.Count == 0);
        }

        /// <summary>
        /// Inserts initialization code the into all the constructors that call the base class constructor. 
        /// </summary>
        /// <param name="classDefinition">The class definition.</param>
        /// <param name="instructionBuilder">The instruction builder that returns the instructions to insert.</param>
        public static void InsertIntoConstructors([NotNull] this TypeDefinition classDefinition, [NotNull] Func<IEnumerable<Instruction>> instructionBuilder)
        {
            foreach (var constructor in classDefinition.GetConstructors())
            {
                var instructions = constructor.Body.Instructions;

                // first call in ctor is the call to base or self constructors.
                var callStatement = instructions.First(item => item.OpCode == OpCodes.Call);
                if (((MethodReference)callStatement.Operand).DeclaringType == classDefinition)
                {
                    // this constructor calls : this(...), no need to initialize here...
                    continue;
                }

                var index = instructions.IndexOf(callStatement) + 1;

                instructions.InsertRange(index, instructionBuilder());
            }
        }
    }
}
