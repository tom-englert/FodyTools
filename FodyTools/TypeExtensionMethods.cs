namespace FodyTools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    using JetBrains.Annotations;

    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using Mono.Cecil.Rocks;
    using Mono.Collections.Generic;

    internal static class TypeExtensionMethods
    {
        private const string FinalizerMethodName = "Finalize";

        /// <summary>
        /// Gets the default constructor of a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The <see cref="MethodDefinition"/> of the default constructor.</returns>
        [CanBeNull]
        public static MethodDefinition GetDefaultConstructor([NotNull] this TypeDefinition type)
        {
            return type.GetConstructors().FirstOrDefault(ctor => ctor.HasBody && ctor.Parameters.Count == 0 && !ctor.IsStatic);
        }

        /// <summary>
        /// Gets the type itself and all base types.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The type and all it's base types.</returns>
        [NotNull]
        public static IEnumerable<TypeDefinition> GetSelfAndBaseTypes([NotNull] this TypeReference type)
        {
            var resolved = type.Resolve();

            yield return resolved;

            var baseType = resolved?.BaseType;

            while (baseType != null)
            {
                resolved = baseType.Resolve();

                yield return resolved;

                baseType = resolved?.BaseType;
            }
        }

        /// <summary>
        /// Gets all base types of the specified type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>All base types.</returns>
        [NotNull]
        public static IEnumerable<TypeDefinition> GetBaseTypes([NotNull] this TypeReference type)
        {
            return type.GetSelfAndBaseTypes().Skip(1);
        }

        /// <summary>
        /// Inserts initialization code the into all the constructors that call the base class constructor. 
        /// </summary>
        /// <param name="classDefinition">The class definition.</param>
        /// <param name="instructionBuilder">The instruction builder that returns the instructions to insert.</param>
        public static void InsertIntoConstructors([NotNull] this TypeDefinition classDefinition, [NotNull] Func<IEnumerable<Instruction>> instructionBuilder)
        {
            foreach (var constructor in classDefinition.GetConstructors().Where(ctor => !ctor.IsStatic))
            {
                var instructions = constructor.Body.Instructions;

                // first call in ctor is the call to base or self constructors.
                var callStatement = instructions.First(item => item.OpCode == OpCodes.Call);
                var method = callStatement.Operand as MethodReference;
                if (method?.Name != ".ctor")
                    throw new InvalidOperationException("Invalid constructor: " + constructor);

                if (method.DeclaringType == classDefinition)
                {
                    // this constructor calls : this(...), no need to initialize here...
                    continue;
                }

                var index = instructions.IndexOf(callStatement) + 1;

                instructions.InsertRange(index, instructionBuilder());
            }
        }

        /// <summary>
        /// Inserts the code at the start of the finalizer. If the class has no finalizer, a default one is created.
        /// </summary>
        /// <param name="classDefinition">The class definition.</param>
        /// <param name="additionalInstructions">The additional instructions to insert.</param>
        /// <exception cref="System.InvalidOperationException">The existing finalizer is invalid.</exception>
        public static void InsertIntoFinalizer([NotNull] this TypeDefinition classDefinition, [NotNull] params Instruction[] additionalInstructions)
        {
            var finalizer = FindFinalizer(classDefinition);

            ExceptionHandler exceptionHandler;
            MethodBody body;
            Collection<Instruction> instructions;

            if (finalizer == null)
            {
                var module = classDefinition.Module;

                var baseFinalizer = module.ImportReference(classDefinition.GetBaseTypes().Select(FindFinalizer).FirstOrDefault(item => item != null));

                const MethodAttributes attributes = MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig;

                finalizer = new MethodDefinition(FinalizerMethodName, attributes, module.TypeSystem.Void);
                finalizer.Overrides.Add(baseFinalizer);

                exceptionHandler = new ExceptionHandler(ExceptionHandlerType.Finally);

                body = finalizer.Body;
                body.ExceptionHandlers.Add(exceptionHandler);

                instructions = body.Instructions;

                var returnStatement = Instruction.Create(OpCodes.Ret);

                instructions.AddRange(
                    Instruction.Create(OpCodes.Leave, returnStatement),
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Call, baseFinalizer),
                    Instruction.Create(OpCodes.Endfinally),
                    returnStatement
                );

                exceptionHandler.TryStart = instructions[0];
                exceptionHandler.TryEnd = instructions[1];
                exceptionHandler.HandlerStart = instructions[1];
                exceptionHandler.HandlerEnd = instructions[4];

                classDefinition.Methods.Add(finalizer);
            }
            else
            {
                body = finalizer.Body;
                instructions = body.Instructions;
                exceptionHandler = body.ExceptionHandlers.FirstOrDefault();
            }

            var start = exceptionHandler?.TryStart;
            var index = instructions.IndexOf(start);

            if (index < 0)
                throw new InvalidOperationException(classDefinition.FullName + ": non-standard Finalizer without valid try/catch block found");

            body.Instructions.InsertRange(index, additionalInstructions);

#pragma warning disable S2259 // Null pointers should not be dereferenced
            exceptionHandler.TryStart = instructions[index];
#pragma warning restore S2259 // Null pointers should not be dereferenced
        }

        /// <summary>
        /// Finds the finalizer method of the specified type.
        /// </summary>
        /// <param name="classDefinition">The class definition.</param>
        /// <returns>The finalizer method, or <c>null</c> if no finalizer exists.</returns>
        public static MethodDefinition FindFinalizer(this TypeDefinition classDefinition)
        {
            return classDefinition.GetMethods().FirstOrDefault(m => m.Name == FinalizerMethodName && !m.HasParameters && (m.Attributes & MethodAttributes.Family) != 0);
        }

        public static void InsertIntoStaticConstructor([NotNull] this TypeDefinition classDefinition, [NotNull] params Instruction[] additionalInstructions)
        {
            var constructor = classDefinition.GetConstructors()
                .FirstOrDefault(ctor => ctor.IsStatic);

            if (constructor == null)
            {
                var module = classDefinition.Module;

                var attributes = MethodAttributes.Private
                    | MethodAttributes.HideBySig
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName;

                constructor = new MethodDefinition(".cctor", attributes, module.TypeSystem.Void);

                classDefinition.Methods.Add(constructor);
                constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            }

            constructor.Body.Instructions.InsertRange(0, additionalInstructions);
        }
    }
}
