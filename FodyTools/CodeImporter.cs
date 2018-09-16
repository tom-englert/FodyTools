namespace FodyTools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;

    using JetBrains.Annotations;

    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using Mono.Cecil.Rocks;

    using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;
    using MethodBody = Mono.Cecil.Cil.MethodBody;

    /// <summary>
    /// A class to import code from one module to another; like e.g. ILMerge, but only imports the specified classes and their local references.
    /// </summary>
    /// <remarks>
    /// The main task of this is to copy code fragments into another module so they can be used by the weaver.
    /// It copies the types specified in the Import method, and automatically copies all required dependencies.
    /// </remarks>
    internal class CodeImporter
    {
        [NotNull]
        private static readonly ConstructorInfo _instructionConstructor = typeof(Instruction).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(OpCode), typeof(object) }, null);

        [NotNull]
        private readonly Dictionary<Assembly, ModuleDefinition> _sourceModuleDefinitions = new Dictionary<Assembly, ModuleDefinition>();
        [NotNull]
        private readonly Dictionary<string, TypeDefinition> _targetTypes = new Dictionary<string, TypeDefinition>();
        [NotNull]
        private readonly Dictionary<MethodDefinition, MethodDefinition> _targetMethods = new Dictionary<MethodDefinition, MethodDefinition>();

        [NotNull]
        private readonly string _targetNamespace;
        [NotNull]
        private readonly ModuleDefinition _targetModule;
        [NotNull]
        private readonly IList<DeferredAction> _deferredActions = new List<DeferredAction>();

        private enum Priority
        {
            Instructions,
            Operands,
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CodeImporter"/> class.
        /// </summary>
        /// <param name="targetModule">The target module into which the specified code will be imported.</param>
        /// <param name="targetNamespace">The target namespace in the target module that will contain the imported classes.</param>
        public CodeImporter([NotNull] ModuleDefinition targetModule, [NotNull] string targetNamespace)
        {
            _targetModule = targetModule;
            _targetNamespace = targetNamespace;
        }

        /// <summary>
        /// Imports the specified type and it's local references from it's source module into the target module.
        /// </summary>
        /// <param name="type">The type to import.</param>
        /// <returns>
        /// The type definition of the imported type in the target module.
        /// </returns>
        [NotNull]
        public TypeDefinition Import([NotNull] Type type)
        {
            var target = ImportType(type);

            while (true)
            {
                var action = _deferredActions.OrderBy(a => (int)a.Priority).FirstOrDefault();
                if (action == null)
                    break;

                _deferredActions.Remove(action);
                action.Action();
            }

            return target;
        }

        /// <summary>
        /// Imports the specified type and it's local references from it's source module into the target module.
        /// </summary>
        /// <typeparam name="T">The type to import.</typeparam>
        /// <returns>
        /// The type definition of the imported type in the target module.
        /// </returns>
        [NotNull]
        public TypeDefinition Import<T>()
        {
            return Import(typeof(T));
        }

        /// <summary>
        /// Imports the methods declaring type into the target module and returns the method definition
        /// of the corresponding method in the target module.
        /// </summary>
        /// <typeparam name="T">The methods return value.</typeparam>
        /// <param name="expression">The method call expression describing the source method.</param>
        /// <returns>The method definition of the imported method.</returns>
        /// <exception cref="ArgumentException">Only method call expression is supported. - expression</exception>
        /// <exception cref="InvalidOperationException">Importing method failed.</exception>
        [NotNull]
        public MethodDefinition ImportMethod<T>([NotNull] Expression<Func<T>> expression)
        {
            if (!(expression.Body is MethodCallExpression methodCall))
                throw new ArgumentException("Only method call expression is supported.", nameof(expression));

            var targetType = Import(methodCall.Method.DeclaringType);
            var methodName = methodCall.Method.Name;

            var argumentTypeNames = methodCall.Arguments.Select(a => a.Type.Name).ToArray();

            return targetType.Methods.Single(m => m.Name == methodName && m.Parameters.Select(p => p.ParameterType.Name).SequenceEqual(argumentTypeNames)) ?? throw new InvalidOperationException("Importing method failed.");
        }

        /// <summary>
        /// Imports the property's declaring type into the target module and returns the property definition
        /// of the corresponding property in the target module.
        /// </summary>
        /// <typeparam name="T">The property type of the property</typeparam>
        /// <param name="expression">The property expression describing the source property.</param>
        /// <returns>The property definition of the imported property</returns>
        /// <exception cref="ArgumentException">
        /// Only a member expression is supported here. - expression
        /// or
        /// Only a property expression is supported here. - expression
        /// </exception>
        [NotNull]
        public PropertyDefinition ImportProperty<T>([NotNull] Expression<Func<T>> expression)
        {
            if (!(expression.Body is MemberExpression memberExpression))
                throw new ArgumentException("Only a member expression is supported here.", nameof(expression));

            var member = memberExpression.Member;
            if (!(member is PropertyInfo))
                throw new ArgumentException("Only a property expression is supported here.", nameof(expression));

            var targetType = Import(member.DeclaringType);
            var propertyName = member.Name;

            return targetType.Properties.Single(m => m.Name == propertyName);
        }

        /// <summary>
        /// Imports the field's declaring type into the target module and returns the field definition
        /// of the corresponding field in the target module.
        /// </summary>
        /// <typeparam name="T">The field type of the field</typeparam>
        /// <param name="expression">The field expression describing the source field.</param>
        /// <returns>The field definition of the imported field</returns>
        /// <exception cref="ArgumentException">
        /// Only a member expression is supported here. - expression
        /// or
        /// Only a field expression is supported here. - expression
        /// </exception>
        [NotNull]
        public FieldDefinition ImportField<T>([NotNull] Expression<Func<T>> expression)
        {
            if (!(expression.Body is MemberExpression memberExpression))
                throw new ArgumentException("Only a member expression is supported here.", nameof(expression));

            var member = memberExpression.Member;
            if (!(member is FieldInfo))
                throw new ArgumentException("Only a field expression is supported here.", nameof(expression));

            var targetType = Import(member.DeclaringType);
            var fieldName = member.Name;

            return targetType.Fields.Single(m => m.Name == fieldName);
        }


        /// <summary>
        /// Registers a source module.<para />
        /// Use this method before importing anything if you want to import types and their dependencies from many source modules.
        /// However in most cases you don't need to call this.
        /// </summary>
        /// <param name="assembly">The assembly of the sources.</param>
        /// <param name="location">Optional; the location. Needs to be specified e.g. if the assembly was not loaded from a file regularly and Assembly.Location is null.</param>
        /// <param name="readSymbols">if set to <c>true</c>, read symbols is enabled when loading the module.</param>
        /// <returns>
        /// The module definition of the source module.
        /// </returns>
        /// <exception cref="InvalidOperationException">Unable get location of assembly " + assembly</exception>
        [NotNull]
        public ModuleDefinition RegisterSourceModule([NotNull] Assembly assembly, [CanBeNull] string location = null, bool readSymbols = true)
        {
            if (_sourceModuleDefinitions.TryGetValue(assembly, out var sourceModule))
                return sourceModule;

            var fileName = location ?? new Uri(assembly.CodeBase, UriKind.Absolute).LocalPath;
            if (string.IsNullOrEmpty(fileName))
                throw new InvalidOperationException("Unable get location of assembly " + assembly);

            sourceModule = ModuleDefinition.ReadModule(fileName, new ReaderParameters { ReadSymbols = readSymbols });
            _sourceModuleDefinitions.Add(assembly, sourceModule);

            // ReSharper disable once AssignNullToNotNullAttribute
            return sourceModule;
        }

        /// <summary>
        /// Registers the source module.<para />
        /// Use this method before importing anything if the source assembly is not available as a file on disk.
        /// </summary>
        /// <param name="assembly">The assembly.</param>
        /// <param name="stream">The stream containing the assembly.</param>
        /// <returns>
        /// The module definition of the source module.
        /// </returns>
        [NotNull]
        public ModuleDefinition RegisterSourceModule([NotNull] Assembly assembly, [NotNull] Stream stream)
        {
            if (_sourceModuleDefinitions.TryGetValue(assembly, out var sourceModule))
                return sourceModule;

            sourceModule = ModuleDefinition.ReadModule(stream);
            _sourceModuleDefinitions.Add(assembly, sourceModule);

            // ReSharper disable once AssignNullToNotNullAttribute
            return sourceModule;
        }

        /// <summary>
        /// Returns a collection of the imported types.
        /// </summary>
        /// <returns>The collection of imported types.</returns>
        [NotNull, ItemNotNull]
        public IReadOnlyCollection<TypeDefinition> ListImportedTypes()
        {
            return _targetTypes.Values
                .Where(t => t.DeclaringType == null)
                .ToArray();
        }

        [NotNull]
        private TypeDefinition ImportType([NotNull] Type type)
        {
            var assembly = type.Assembly;

            var sourceModule = RegisterSourceModule(assembly);

            var sourceType = sourceModule.GetType(type.FullName);

            if (sourceType == null)
                throw new InvalidOperationException("Did not find type " + type.FullName + " in module " + sourceModule.FileName);

            return ImportTypeDefinition(sourceType);
        }

        [ContractAnnotation("sourceType:notnull=>notnull")]
        private TypeDefinition ImportTypeDefinition(TypeDefinition sourceType)
        {
            if (sourceType == null)
                return null;

            if (_targetTypes.TryGetValue(sourceType.FullName, out var targetType))
                return targetType;

            var declaringType = ImportTypeDefinition(sourceType.DeclaringType);
            var targetNamespace = declaringType != null ? null : _targetNamespace;

            targetType = new TypeDefinition(targetNamespace, sourceType.Name, sourceType.Attributes) { DeclaringType = declaringType };

            _targetTypes.Add(sourceType.FullName, targetType);

            foreach (var sourceTypeInterface in sourceType.Interfaces)
            {
                targetType.Interfaces.Add(new InterfaceImplementation(ImportType(sourceTypeInterface.InterfaceType, null)));
            }

            CopyGenericParameters(sourceType, targetType);
            CopyAttributes(sourceType, targetType);

            targetType.BaseType = ImportType(sourceType.BaseType, null);

            if (declaringType != null)
            {
                declaringType.NestedTypes.Add(targetType);
            }
            else
            {
                _targetModule.Types.Add(targetType);
            }

            CopyFields(sourceType, targetType);
            CopyMethods(sourceType, targetType);
            CopyProperties(sourceType, targetType);

            return targetType;
        }

        private void CopyMethods([NotNull] TypeDefinition source, [NotNull] TypeDefinition target)
        {
            foreach (var method in source.Methods)
            {
                ImportMethodDefinition(method, target);
            }
        }

        private void CopyProperties([NotNull] TypeDefinition source, [NotNull] TypeDefinition target)
        {
            foreach (var sourceDefinition in source.Properties)
            {
                var targetDefinition = new PropertyDefinition(sourceDefinition.Name, sourceDefinition.Attributes, ImportType(sourceDefinition.PropertyType, null))
                {
                    GetMethod = ImportMethodDefinition(sourceDefinition.GetMethod, target),
                    SetMethod = ImportMethodDefinition(sourceDefinition.SetMethod, target)
                };

                CopyAttributes(sourceDefinition, targetDefinition);

                target.Properties.Add(targetDefinition);
            }
        }

        private void CopyFields([NotNull] TypeDefinition source, [NotNull] TypeDefinition target)
        {
            foreach (var sourceDefinition in source.Fields)
            {
                var targetDefinition = new FieldDefinition(sourceDefinition.Name, sourceDefinition.Attributes, ImportType(sourceDefinition.FieldType, null));

                CopyAttributes(sourceDefinition, targetDefinition);

                target.Fields.Add(targetDefinition);
            }
        }

        private MethodDefinition ImportMethodDefinition([CanBeNull] MethodDefinition source, [NotNull] TypeDefinition targetType)
        {
            if (source == null)
                return null;

            if (_targetMethods.TryGetValue(source, out var target))
                return target;

            target = new MethodDefinition(source.Name, source.Attributes, TemporaryPlaceholderType)
            {
                ImplAttributes = source.ImplAttributes,
            };

            _targetMethods.Add(source, target);

            foreach (var sourceOverride in source.Overrides)
            {
                target.Overrides.Add(ImportMethodReference(sourceOverride));
            }

            CopyAttributes(source, target);
            CopyGenericParameters(source, target);
            CopyParameters(source, target);

            targetType.Methods.Add(target);

            if (source.IsPInvokeImpl)
            {
                var moduleRef = _targetModule.ModuleReferences
                    .FirstOrDefault(mr => mr.Name == source.PInvokeInfo.Module.Name);

                if (moduleRef == null)
                {
                    moduleRef = new ModuleReference(source.PInvokeInfo.Module.Name);
                    _targetModule.ModuleReferences.Add(moduleRef);
                }

                target.PInvokeInfo = new PInvokeInfo(source.PInvokeInfo.Attributes, source.PInvokeInfo.EntryPoint, moduleRef);
            }

            target.ReturnType = ImportType(source.ReturnType, target);

            ImportMethodBody(source, target);

            return target;
        }

        private void CopyAttributes([NotNull] ICustomAttributeProvider source, ICustomAttributeProvider target)
        {
            if (source.HasCustomAttributes)
            {
                foreach (var customAttribute in source.CustomAttributes)
                {
                    var attributeType = ImportType(customAttribute.AttributeType, null);

                    target.CustomAttributes.Add(new CustomAttribute(_targetModule.ImportReference(attributeType.Resolve().GetConstructors().FirstOrDefault()), customAttribute.GetBlob()));
                }
            }
        }

        private void ImportMethodBody([NotNull] MethodDefinition source, [NotNull] MethodDefinition target)
        {
            if (!source.HasBody)
                return;

            var sourceMethodBody = source.Body;
            var targeMethodBody = target.Body;

            targeMethodBody.InitLocals = sourceMethodBody.InitLocals;

            foreach (var sourceVariable in sourceMethodBody.Variables)
            {
                targeMethodBody.Variables.Add(new VariableDefinition(ImportType(sourceVariable.VariableType, target)));
            }

            ExecuteDeferred(Priority.Instructions, () => CopyInstructions(source, target));
        }

        private void CopyParameters([NotNull] MethodReference sourceMethod, [NotNull] MethodReference targetMethod)
        {
            foreach (var sourceParameter in sourceMethod.Parameters)
            {
                targetMethod.Parameters.Add(new ParameterDefinition(sourceParameter.Name, sourceParameter.Attributes, ImportType(sourceParameter.ParameterType, targetMethod)));
            }
        }

        private void CopyGenericParameters([NotNull] TypeDefinition source, [NotNull] TypeDefinition target)
        {
            if (!source.HasGenericParameters)
                return;

            foreach (var genericParameter in source.GenericParameters)
            {
                var parameter = new GenericParameter(genericParameter.Name, ImportType(genericParameter.DeclaringType, null));
                if (genericParameter.HasConstraints)
                {
                    foreach (var constraint in genericParameter.Constraints)
                    {
                        parameter.Constraints.Add(ImportType(constraint.GetElementType(), null));
                    }
                }

                target.GenericParameters.Add(parameter);
            }
        }

        private void CopyGenericParameters([NotNull] MethodReference source, [NotNull] MethodReference target)
        {
            if (source.HasGenericParameters)
            {
                foreach (var genericParameter in source.GenericParameters)
                {
                    var provider = genericParameter.Type == GenericParameterType.Method
                        ? (IGenericParameterProvider)target
                        : ImportType(genericParameter.DeclaringType, null);

                    var parameter = new GenericParameter(genericParameter.Name, provider)
                    {
                        Attributes = genericParameter.Attributes
                    };

                    if (genericParameter.HasConstraints)
                    {
                        foreach (var constraint in genericParameter.Constraints)
                        {
                            parameter.Constraints.Add(ImportType(constraint.GetElementType(), target));
                        }
                    }

                    target.GenericParameters.Add(parameter);
                }
            }
        }

        private void CopyExceptionHandlers([NotNull] MethodBody source, [NotNull] MethodBody target)
        {
            if (!source.HasExceptionHandlers)
            {
                return;
            }

            foreach (var sourceHandler in source.ExceptionHandlers)
            {
                var targetHandler = new ExceptionHandler(sourceHandler.HandlerType);
                var sourceInstructions = source.Instructions;
                var targetInstructions = target.Instructions;

                if (sourceHandler.TryStart != null)
                {
                    targetHandler.TryStart = targetInstructions[sourceInstructions.IndexOf(sourceHandler.TryStart)];
                }
                if (sourceHandler.TryEnd != null)
                {
                    targetHandler.TryEnd = targetInstructions[sourceInstructions.IndexOf(sourceHandler.TryEnd)];
                }
                if (sourceHandler.HandlerStart != null)
                {
                    targetHandler.HandlerStart = targetInstructions[sourceInstructions.IndexOf(sourceHandler.HandlerStart)];
                }
                if (sourceHandler.HandlerEnd != null)
                {
                    targetHandler.HandlerEnd = targetInstructions[sourceInstructions.IndexOf(sourceHandler.HandlerEnd)];
                }
                if (sourceHandler.FilterStart != null)
                {
                    targetHandler.FilterStart = targetInstructions[sourceInstructions.IndexOf(sourceHandler.FilterStart)];
                }
                if (sourceHandler.CatchType != null)
                {
                    targetHandler.CatchType = ImportType(sourceHandler.CatchType, null);
                }

                target.ExceptionHandlers.Add(targetHandler);
            }
        }

        private void CopyInstructions([NotNull] MethodDefinition source, [NotNull] MethodDefinition target)
        {
            var targetDebugInformation = target.DebugInformation;
            var sourceDebugInformation = source.DebugInformation;

            foreach (var sourceInstruction in source.Body.Instructions)
            {
                var targetInstruction = CloneInstruction(sourceInstruction, target);

                target.Body.Instructions.Add(targetInstruction);

                var sequencePoint = sourceDebugInformation?.GetSequencePoint(sourceInstruction);
                if (sequencePoint != null)
                    targetDebugInformation?.SequencePoints?.Add(CloneSequencePoint(targetInstruction, sequencePoint));
            }

            CopyExceptionHandlers(source.Body, target.Body);

            if (true == targetDebugInformation?.HasSequencePoints)
            {
                var scope = targetDebugInformation.Scope = new ScopeDebugInformation(target.Body.Instructions.First(), target.Body.Instructions.Last());

                foreach (var variable in sourceDebugInformation.Scope.Variables)
                {
                    var targetVariable = target.Body.Variables[variable.Index];

                    scope.Variables.Add(new VariableDebugInformation(targetVariable, variable.Name));
                }
            }
        }

        [NotNull]
        private Instruction CloneInstruction([NotNull] Instruction source, [NotNull] MethodDefinition targetMethod)
        {
            var targetInstruction = (Instruction)_instructionConstructor.Invoke(new[] { source.OpCode, source.Operand });

            switch (targetInstruction.Operand)
            {
                case MethodDefinition sourceMethodDefinition:
                    ExecuteDeferred(Priority.Operands, () => targetInstruction.Operand = ImportMethodDefinition(sourceMethodDefinition, targetMethod.DeclaringType));
                    break;

                case GenericInstanceMethod genericInstanceMethod:
                    ExecuteDeferred(Priority.Operands, () => targetInstruction.Operand = ImportGenericInstanceMethod(genericInstanceMethod, targetMethod.DeclaringType));
                    break;

                case MethodReference sourceMethodReference:
                    ExecuteDeferred(Priority.Operands, () => targetInstruction.Operand = ImportMethodReference(sourceMethodReference));
                    break;

                case TypeReference typeReference:
                    targetInstruction.Operand = ImportType(typeReference, targetMethod);
                    break;

                case FieldReference fieldReference:
                    ExecuteDeferred(Priority.Operands, () => targetInstruction.Operand = new FieldReference(fieldReference.Name, ImportType(fieldReference.FieldType, targetMethod), ImportType(fieldReference.DeclaringType, targetMethod)));
                    break;
            }

            return targetInstruction;
        }

        [NotNull]
        private static SequencePoint CloneSequencePoint([NotNull] Instruction instruction, [NotNull] SequencePoint sequencePoint)
        {
            return new SequencePoint(instruction, sequencePoint.Document)
            {
                StartLine = sequencePoint.StartLine,
                StartColumn = sequencePoint.StartColumn,
                EndLine = sequencePoint.EndLine,
                EndColumn = sequencePoint.EndColumn,
            };
        }

        private TypeReference ImportType([CanBeNull] TypeReference source, [CanBeNull] MethodReference targetMethod)
        {
            switch (source)
            {
                case null:
                    return null;

                case TypeDefinition typeDefinition:
                    return ImportTypeDefinition(typeDefinition);

                case GenericParameter genericParameter:
                    return ImportGenericParameter(genericParameter, targetMethod);

                case GenericInstanceType genericInstanceType:
                    return ImportGenericInstanceType(genericInstanceType, targetMethod);

                case ByReferenceType byReferenceType:
                    return new ByReferenceType(ImportType(byReferenceType.ElementType, targetMethod));

                case ArrayType arrayType:
                    return new ArrayType(ImportType(arrayType.ElementType, targetMethod), arrayType.Rank);

                default:
                    return ImportTypeReference(source);
            }
        }

        [NotNull]
        private TypeReference ImportTypeReference([NotNull] TypeReference source)
        {
            Debug.Assert(source.GetType() == typeof(TypeReference));

            if (IsExternalReference(source))
                return _targetModule.ImportReference(source);

            return new TypeReference(_targetNamespace, source.Name, _targetModule, source.Scope, source.IsValueType);
        }

        [NotNull]
        private TypeReference ImportGenericInstanceType([NotNull] GenericInstanceType source, MethodReference targetMethod)
        {
            var target = new GenericInstanceType(ImportType(source.ElementType, targetMethod));

            foreach (var genericArgument in source.GenericArguments)
            {
                target.GenericArguments.Add(ImportType(genericArgument, targetMethod));
            }

            return target;
        }

        [NotNull]
        private TypeReference ImportGenericParameter([NotNull] GenericParameter source, [CanBeNull] MethodReference targetMethod)
        {
            var genericParameterProvider = (source.Type == GenericParameterType.Method)
                ? (targetMethod ?? throw new InvalidOperationException("Need a method reference for generic method parameter."))
                : (IGenericParameterProvider)ImportType(source.DeclaringType, targetMethod);

            var index = source.Position;

            if (index < genericParameterProvider.GenericParameters.Count)
                return genericParameterProvider.GenericParameters[index];

            return source;
        }

        [NotNull]
        private MethodReference ImportMethodReference([NotNull] MethodReference source)
        {
            Debug.Assert(source.GetType() == typeof(MethodReference));

            var target = new MethodReference(source.Name, TemporaryPlaceholderType)
            {
                HasThis = source.HasThis,
                ExplicitThis = source.ExplicitThis,
                CallingConvention = source.CallingConvention
            };

            CopyGenericParameters(source, target);
            CopyParameters(source, target);

            target.DeclaringType = ImportType(source.DeclaringType, target);
            target.ReturnType = ImportType(source.ReturnType, target);

            return target;
        }

        [NotNull]
        private MethodReference ImportGenericInstanceMethod([NotNull] GenericInstanceMethod source, TypeDefinition targetType)
        {
            var elementMethod = source.ElementMethod;

            switch (source.ElementMethod)
            {
                case MethodDefinition sourceMethodDefinition:
                    elementMethod = ImportMethodDefinition(sourceMethodDefinition, targetType);
                    break;

                case GenericInstanceMethod genericInstanceMethod:
                    elementMethod = ImportGenericInstanceMethod(genericInstanceMethod, targetType);
                    break;

                case MethodReference sourceMethodReference:
                    elementMethod = ImportMethodReference(sourceMethodReference);
                    break;
            }

            var target = new GenericInstanceMethod(elementMethod);

            foreach (var genericArgument in source.GenericArguments)
            {
                target.GenericArguments.Add(ImportType(genericArgument, target));
            }

            return target;
        }

        private bool IsExternalReference([NotNull] TypeReference typeReference)
        {
            var moduleDefinition = typeReference.Resolve()?.Module;

            return _targetModule != moduleDefinition && !_sourceModuleDefinitions.ContainsValue(moduleDefinition);
        }

        private void ExecuteDeferred(Priority priority, [NotNull] Action action)
        {
            _deferredActions.Add(new DeferredAction(priority, action));
        }

        [NotNull]
        private TypeReference TemporaryPlaceholderType => new TypeReference("temporary", "type", _targetModule, _targetModule);

        private class DeferredAction
        {
            public DeferredAction(Priority priority, [NotNull] Action action)
            {
                Priority = priority;
                Action = action;
            }

            public Priority Priority { get; }

            [NotNull]
            public Action Action { get; }
        }
    }
}
