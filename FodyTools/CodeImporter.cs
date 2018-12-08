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
    internal sealed class CodeImporter
    {
        [NotNull]
        private static readonly ConstructorInfo _instructionConstructor = typeof(Instruction).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(OpCode), typeof(object) }, null);

        [NotNull]
        private readonly Dictionary<string, ModuleDefinition> _sourceModuleDefinitions = new Dictionary<string, ModuleDefinition>();

        [NotNull]
        private readonly Dictionary<string, TypeDefinition> _targetTypesBySourceName = new Dictionary<string, TypeDefinition>();

        [NotNull]
        private readonly HashSet<TypeDefinition> _targetTypes = new HashSet<TypeDefinition>();

        [NotNull]
        private readonly Dictionary<MethodDefinition, MethodDefinition> _targetMethods = new Dictionary<MethodDefinition, MethodDefinition>();

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
        public CodeImporter([NotNull] ModuleDefinition targetModule)
        {
            TargetModule = targetModule;
        }

        [NotNull]
        public ModuleDefinition TargetModule { get; }

        [CanBeNull]
        public IModuleResolver ModuleResolver { get; set; }

        public bool HideImportedTypes { get; set; } = true;

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
            return ImportType(type);
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

        [NotNull]
        public TypeDefinition Import([NotNull] TypeDefinition sourceType)
        {
            RegisterSourceModule(sourceType.Module);

            return ProcessDeferredActions(ImportTypeDefinition(sourceType));
        }

        [ContractAnnotation("typeReference:notnull=>notnull")]
        public TypeReference ImportTypeReference([CanBeNull] TypeReference typeReference)
        {
            if (typeReference == null)
                return null;

            if (typeReference is GenericInstanceType genericInstanceType)
            {
                var target = new GenericInstanceType(ImportTypeReference(genericInstanceType.ElementType));

                foreach (var genericArgument in genericInstanceType.GenericArguments)
                {
                    target.GenericArguments.Add(ImportTypeReference(genericArgument));
                }

                return target;
            }

            if (IsLocalOrExternalReference(typeReference))
                return typeReference;

            var typeDefinition = typeReference.Resolve();

            if (typeDefinition == null)
                return typeReference;

            return ProcessDeferredActions(ImportTypeDefinition(typeDefinition));
        }

        [NotNull]
        public GenericInstanceMethod Import([NotNull] GenericInstanceMethod method)
        {
            return ProcessDeferredActions(ImportGenericInstanceMethod(method));
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
            return ImportMethodInternal(expression);
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
        public MethodDefinition ImportMethod([NotNull] Expression<Action> expression)
        {
            return ImportMethodInternal(expression);
        }

        [NotNull]
        private MethodDefinition ImportMethodInternal([NotNull] LambdaExpression expression)
        {
            expression.GetMethodInfo(out var declaringType, out var methodName, out var argumentTypes);

            var targetType = Import(declaringType);

            return targetType.Methods.Single(m => m.Name == methodName && m.Parameters.ParametersMatch(argumentTypes)) ?? throw new InvalidOperationException("Importing method failed.");
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
        /// Imports the event's declaring type into the target module and returns the event definition
        /// of the corresponding event in the target module.
        /// </summary>
        /// <typeparam name="T">The event type of the event</typeparam>
        /// <param name="expression">The event expression describing the source event.</param>
        /// <returns>The event definition of the imported event</returns>
        /// <exception cref="ArgumentException">
        /// Only a member expression is supported here. - expression
        /// or
        /// Only a event expression is supported here. - expression
        /// </exception>
        [NotNull]
        public EventDefinition ImportEvent<T>([NotNull] Expression<Func<T>> expression)
        {
            if (!(expression.Body is MemberExpression memberExpression))
                throw new ArgumentException("Only a member expression is supported here.", nameof(expression));

            var member = memberExpression.Member;
            if (!(member is EventInfo))
                throw new ArgumentException("Only a event expression is supported here.", nameof(expression));

            var targetType = Import(member.DeclaringType);
            var eventName = member.Name;

            return targetType.Events.Single(m => m.Name == eventName);
        }

        /// <summary>
        /// Returns a collection of the imported types.
        /// </summary>
        /// <returns>The collection of imported types.</returns>
        [NotNull]
        public IDictionary<string, TypeDefinition> ListImportedTypes()
        {
            return _targetTypesBySourceName
                .Where(t => t.Value?.DeclaringType == null)
                .ToDictionary(item => item.Key, item => item.Value);
        }

        [NotNull]
        public ICollection<ModuleDefinition> ListImportedModules()
        {
            return _sourceModuleDefinitions.Values;
        }

        [NotNull]
        private ModuleDefinition RegisterSourceModule([NotNull] Assembly assembly)
        {
            var assemblyName = assembly.FullName;

            if (_sourceModuleDefinitions.TryGetValue(assemblyName, out var sourceModule) && (sourceModule != null))
                return sourceModule;

            var fileName = new Uri(assembly.CodeBase, UriKind.Absolute).LocalPath;
            if (string.IsNullOrEmpty(fileName))
                throw new InvalidOperationException("Unable get location of assembly " + assembly);

            sourceModule = ModuleDefinition.ReadModule(fileName);

            try
            {
                sourceModule.ReadSymbols();
            }
            catch
            {
                // module has no symbols, just go without...
            }

            _sourceModuleDefinitions[assemblyName] = sourceModule;

            // ReSharper disable once AssignNullToNotNullAttribute
            return sourceModule;
        }

        public void RegisterSourceModule([NotNull] ModuleDefinition sourceModule)
        {
            var assemblyName = sourceModule.Assembly.FullName;

            if (_sourceModuleDefinitions.ContainsKey(assemblyName))
                return;

            if (!sourceModule.HasSymbols)
            {
                try
                {
                    sourceModule.ReadSymbols();
                }
                catch
                {
                    // module has no symbols, just go without...
                }
            }

            _sourceModuleDefinitions[assemblyName] = sourceModule;
        }

        [NotNull]
        private TypeDefinition ImportType([NotNull] Type type)
        {
            var assembly = type.Assembly;

            var sourceModule = RegisterSourceModule(assembly);

            var sourceType = sourceModule.GetType(type.GetFullName());

            if (sourceType == null)
                throw new InvalidOperationException("Did not find type " + type.GetFullName() + " in module " + sourceModule.FileName);

            return ProcessDeferredActions(ImportTypeDefinition(sourceType));
        }

        [ContractAnnotation("sourceType:notnull=>notnull")]
        private TypeDefinition ImportTypeDefinition(TypeDefinition sourceType)
        {
            if (sourceType == null)
                return null;

            if (_targetTypesBySourceName.TryGetValue(sourceType.FullName, out var targetType))
                return targetType;

            if (_targetTypes.Contains(sourceType))
                return sourceType;

            if (IsLocalOrExternalReference(sourceType))
                return sourceType;

            RegisterSourceModule(sourceType.Module);

            targetType = new TypeDefinition(sourceType.Namespace, sourceType.Name, sourceType.Attributes);

            _targetTypesBySourceName.Add(sourceType.FullName, targetType);
            _targetTypes.Add(targetType);

            targetType.DeclaringType = ImportTypeDefinition(sourceType.DeclaringType);

            foreach (var sourceTypeInterface in sourceType.Interfaces)
            {
                targetType.Interfaces.Add(new InterfaceImplementation(ImportType(sourceTypeInterface.InterfaceType, null)));
            }

            CopyGenericParameters(sourceType, targetType);
            CopyAttributes(sourceType, targetType);

            targetType.BaseType = ImportType(sourceType.BaseType, null);

            if (targetType.IsNested)
            {
                targetType.DeclaringType.NestedTypes.Add(targetType);
            }
            else
            {
                if (HideImportedTypes)
                {
                    targetType.IsPublic = false;
                }

                TargetModule.Types.Add(targetType);
            }

            CopyFields(sourceType, targetType);
            CopyMethods(sourceType, targetType);
            CopyProperties(sourceType, targetType);
            CopyEvents(sourceType, targetType);

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

        private void CopyEvents([NotNull] TypeDefinition source, [NotNull] TypeDefinition target)
        {
            foreach (var sourceDefinition in source.Events)
            {
                var targetDefinition = new EventDefinition(sourceDefinition.Name, sourceDefinition.Attributes, ImportType(sourceDefinition.EventType, null))
                {
                    AddMethod = ImportMethodDefinition(sourceDefinition.AddMethod, target),
                    RemoveMethod = ImportMethodDefinition(sourceDefinition.RemoveMethod, target)
                };

                CopyAttributes(sourceDefinition, targetDefinition);

                target.Events.Add(targetDefinition);
            }
        }

        private void CopyFields([NotNull] TypeDefinition source, [NotNull] TypeDefinition target)
        {
            foreach (var sourceDefinition in source.Fields)
            {
                var fieldName = sourceDefinition.Name;

                var targetDefinition = new FieldDefinition(fieldName, sourceDefinition.Attributes, ImportType(sourceDefinition.FieldType, null))
                {
                    InitialValue = sourceDefinition.InitialValue,
                    Offset = sourceDefinition.Offset,
                };

                if (sourceDefinition.HasConstant)
                {
                    targetDefinition.Constant = sourceDefinition.Constant;
                }

                if (sourceDefinition.HasMarshalInfo)
                {
                    targetDefinition.MarshalInfo = sourceDefinition.MarshalInfo;
                }

                CopyAttributes(sourceDefinition, targetDefinition);

                target.Fields.Add(targetDefinition);
            }
        }

        private MethodDefinition ImportMethodDefinition([CanBeNull] MethodDefinition sourceDefinition, [NotNull] TypeDefinition targetType)
        {
            if (sourceDefinition == null)
                return null;

            if (IsLocalOrExternalReference(sourceDefinition.DeclaringType))
                return sourceDefinition;

            if (_targetMethods.TryGetValue(sourceDefinition, out var target))
                return target;

            target = new MethodDefinition(sourceDefinition.Name, sourceDefinition.Attributes, TemporaryPlaceholderType)
            {
                ImplAttributes = sourceDefinition.ImplAttributes,
            };

            _targetMethods.Add(sourceDefinition, target);

            foreach (var sourceOverride in sourceDefinition.Overrides)
            {
                switch (sourceOverride)
                {
                    case MethodDefinition methodDefinition:
                        target.Overrides.Add(ImportMethodDefinition(methodDefinition, targetType));
                        break;

                    default:
                        target.Overrides.Add(ImportMethodReference(sourceOverride));
                        break;
                }
            }

            CopyAttributes(sourceDefinition, target);
            CopyGenericParameters(sourceDefinition, target);
            CopyParameters(sourceDefinition, target);

            targetType.Methods.Add(target);

            if (sourceDefinition.IsPInvokeImpl)
            {
                var moduleRef = TargetModule.ModuleReferences
                    .FirstOrDefault(mr => mr.Name == sourceDefinition.PInvokeInfo.Module.Name);

                if (moduleRef == null)
                {
                    moduleRef = new ModuleReference(sourceDefinition.PInvokeInfo.Module.Name);
                    TargetModule.ModuleReferences.Add(moduleRef);
                }

                target.PInvokeInfo = new PInvokeInfo(sourceDefinition.PInvokeInfo.Attributes, sourceDefinition.PInvokeInfo.EntryPoint, moduleRef);
            }

            target.ReturnType = ImportType(sourceDefinition.ReturnType, target);

            ImportMethodBody(sourceDefinition, target);

            return target;
        }

        private void CopyAttributes([NotNull] ICustomAttributeProvider source, ICustomAttributeProvider target)
        {
            if (source.HasCustomAttributes)
            {
                foreach (var customAttribute in source.CustomAttributes)
                {
                    var attributeType = ImportType(customAttribute.AttributeType, null);

                    var constructor = attributeType.Resolve().GetConstructors().Where(ctor => !ctor.IsStatic).FirstOrDefault();
                    if (constructor != null)
                    {
                        target.CustomAttributes.Add(new CustomAttribute(TargetModule.ImportReference(constructor), customAttribute.GetBlob()));
                    }
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
                var targetParameter = new ParameterDefinition(sourceParameter.Name, sourceParameter.Attributes, ImportType(sourceParameter.ParameterType, targetMethod));

                CopyAttributes(sourceParameter, targetParameter);

                if (sourceParameter.HasMarshalInfo)
                {
                    targetParameter.MarshalInfo = sourceParameter.MarshalInfo;
                }

                targetMethod.Parameters.Add(targetParameter);
            }
        }

        private void CopyGenericParameters([NotNull] TypeDefinition source, [NotNull] TypeDefinition target)
        {
            if (!source.HasGenericParameters)
                return;

            foreach (var genericParameter in source.GenericParameters)
            {
                var parameter = new GenericParameter(genericParameter.Name, ImportType(genericParameter.DeclaringType, null))
                {
                    Attributes = genericParameter.Attributes
                };

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

            var sourceBody = source.Body;
            var targetBody = target.Body;

            var sourceInstructions = sourceBody.Instructions;
            var targetInstructions = targetBody.Instructions;

            var instructionMap = new Dictionary<Instruction, Instruction>();

            foreach (var sourceInstruction in sourceInstructions)
            {
                var targetInstruction = CloneInstruction(sourceInstruction, target, instructionMap);

                instructionMap.Add(sourceInstruction, targetInstruction);

                targetInstructions.Add(targetInstruction);

                var sequencePoint = sourceDebugInformation?.GetSequencePoint(sourceInstruction);
                if (sequencePoint != null)
                    targetDebugInformation?.SequencePoints?.Add(CloneSequencePoint(targetInstruction, sequencePoint));
            }

            CopyExceptionHandlers(sourceBody, targetBody);

            if (true == targetDebugInformation?.HasSequencePoints)
            {
                var scope = targetDebugInformation.Scope = new ScopeDebugInformation(targetInstructions.First(), targetInstructions.Last());

                foreach (var variable in sourceDebugInformation.Scope.Variables)
                {
                    var targetVariable = targetBody.Variables[variable.Index];

                    scope.Variables.Add(new VariableDebugInformation(targetVariable, variable.Name));
                }
            }
        }

        [NotNull]
        private Instruction CloneInstruction([NotNull] Instruction source, [NotNull] MethodDefinition targetMethod, [NotNull] Dictionary<Instruction, Instruction> instructionMap)
        {
            var targetInstruction = (Instruction)_instructionConstructor.Invoke(new[] { source.OpCode, source.Operand });

            switch (targetInstruction.Operand)
            {
                case MethodDefinition sourceMethodDefinition:
                    var targetType = ImportTypeDefinition(sourceMethodDefinition.DeclaringType);
                    ExecuteDeferred(Priority.Operands, () => targetInstruction.Operand = ImportMethodDefinition(sourceMethodDefinition, targetType));
                    break;

                case GenericInstanceMethod genericInstanceMethod:
                    ExecuteDeferred(Priority.Operands, () => targetInstruction.Operand = ImportGenericInstanceMethod(genericInstanceMethod));
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

                case Instruction instruction:
                    ExecuteDeferred(Priority.Operands, () => targetInstruction.Operand = instructionMap[instruction]);
                    break;

                case Instruction[] instructions:
                    ExecuteDeferred(Priority.Operands, () => targetInstruction.Operand = instructions.Select(instruction => instructionMap[instruction]).ToArray());
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

                case RequiredModifierType requiredModifierType:
                    return new RequiredModifierType(ImportType(requiredModifierType.ModifierType, targetMethod), ImportType(requiredModifierType.ElementType, targetMethod));

                default:
                    return ImportTypeReference(source, targetMethod);
            }
        }

        [NotNull]
        private TypeReference ImportTypeReference([NotNull] TypeReference source, [CanBeNull] MethodReference targetMethod)
        {
            Debug.Assert((source.GetType() == typeof(TypeReference)) || (source is TypeSpecification));

            if (IsLocalOrExternalReference(source))
            {
                return TargetModule.ImportReference(source);
            }

            var typeDefinition = source.Resolve();

            if (typeDefinition == null)
                throw new InvalidOperationException($"Unable to resolve type {source}");

            return ImportType(typeDefinition, targetMethod);
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
        private GenericInstanceMethod ImportGenericInstanceMethod([NotNull] GenericInstanceMethod source)
        {
            var elementMethod = source.ElementMethod;

            switch (source.ElementMethod)
            {
                case MethodDefinition sourceMethodDefinition:
                    elementMethod = ImportMethodDefinition(sourceMethodDefinition, ImportTypeDefinition(sourceMethodDefinition.DeclaringType));
                    break;

                case GenericInstanceMethod genericInstanceMethod:
                    elementMethod = ImportGenericInstanceMethod(genericInstanceMethod);
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

        private bool IsLocalOrExternalReference([NotNull] TypeReference typeReference)
        {
            var scope = typeReference.Scope;

            string assemblyName;

            switch (scope)
            {
                case AssemblyNameReference assemblyNameReference:
                    assemblyName = assemblyNameReference.FullName;
                    break;

                case ModuleDefinition moduleDefinition:
                    assemblyName = moduleDefinition.Assembly.FullName;
                    break;

                default:
                    return false;
            }

            if (string.Equals(TargetModule.Assembly.FullName, assemblyName, StringComparison.OrdinalIgnoreCase))
                return true;

            return !_sourceModuleDefinitions.ContainsKey(assemblyName)
                && !ResolveModule(typeReference, assemblyName);
        }

        private bool ResolveModule([NotNull] TypeReference typeReference, [NotNull] string assemblyName)
        {
            var module = ModuleResolver?.Resolve(typeReference, assemblyName);

            if (module != null)
            {
                RegisterSourceModule(module);
                return true;
            }

            return false;
        }

        private void ExecuteDeferred(Priority priority, [NotNull] Action action)
        {
            _deferredActions.Add(new DeferredAction(priority, action));
        }

        [NotNull]
        private TypeReference TemporaryPlaceholderType => new TypeReference("temporary", "type", TargetModule, TargetModule);

        [ContractAnnotation("target:notnull=>notnull")]
        private T ProcessDeferredActions<T>(T target)
            where T : class
        {
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

    internal static class CodeImporterExtensions
    {
        public static void ILMerge([NotNull] this CodeImporter codeImporter)
        {
            var module = codeImporter.TargetModule;

            var existingTypes = module.GetTypes().ToArray();

            MergeAttributes(codeImporter, module);
            MergeAttributes(codeImporter, module.Assembly);

            foreach (var typeDefinition in existingTypes)
            {
                MergeAttributes(codeImporter, typeDefinition);
                MergeGenericParameters(codeImporter, typeDefinition);

                typeDefinition.BaseType = codeImporter.ImportTypeReference(typeDefinition.BaseType);

                foreach (var fieldDefinition in typeDefinition.Fields)
                {
                    MergeAttributes(codeImporter, fieldDefinition);
                    fieldDefinition.FieldType = codeImporter.ImportTypeReference(fieldDefinition.FieldType);
                }

                foreach (var eventDefinition in typeDefinition.Events)
                {
                    MergeAttributes(codeImporter, eventDefinition);
                    eventDefinition.EventType = codeImporter.ImportTypeReference(eventDefinition.EventType);
                }

                foreach (var propertyDefinition in typeDefinition.Properties)
                {
                    MergeAttributes(codeImporter, propertyDefinition);

                    propertyDefinition.PropertyType = codeImporter.ImportTypeReference(propertyDefinition.PropertyType);

                    if (!propertyDefinition.HasParameters)
                        continue;

                    foreach (var parameter in propertyDefinition.Parameters)
                    {
                        MergeAttributes(codeImporter, parameter);
                        parameter.ParameterType = codeImporter.ImportTypeReference(parameter.ParameterType);
                    }
                }

                foreach (var methodDefinition in typeDefinition.Methods)
                {
                    MergeAttributes(codeImporter, methodDefinition);
                    MergeGenericParameters(codeImporter, methodDefinition);

                    methodDefinition.ReturnType = codeImporter.ImportTypeReference(methodDefinition.ReturnType);

                    foreach (var parameter in methodDefinition.Parameters)
                    {
                        MergeAttributes(codeImporter, parameter);
                        parameter.ParameterType = codeImporter.ImportTypeReference(parameter.ParameterType);
                    }

                    var methodBody = methodDefinition.Body;
                    if (methodBody == null)
                        continue;

                    foreach (var variable in methodBody.Variables)
                    {
                        variable.VariableType = codeImporter.ImportTypeReference(variable.VariableType);
                    }

                    foreach (var instruction in methodBody.Instructions)
                    {
                        switch (instruction.Operand)
                        {
                            case MethodDefinition _:
                                break;

                            case GenericInstanceMethod genericInstanceMethod:
                                instruction.Operand = codeImporter.Import(genericInstanceMethod);
                                break;

                            case MethodReference methodReference:
                                // instruction.Operand = codeImporter.ImportMethodReference(methodReference);
                                methodReference.DeclaringType = codeImporter.ImportTypeReference(methodReference.DeclaringType);
                                methodReference.ReturnType = codeImporter.ImportTypeReference(methodReference.ReturnType);
                                foreach (var parameter in methodReference.Parameters)
                                {
                                    parameter.ParameterType = codeImporter.ImportTypeReference(parameter.ParameterType);
                                }
                                break;

                            case ArrayType arrayType:
                                instruction.Operand = new ArrayType(codeImporter.ImportTypeReference(arrayType.ElementType), arrayType.Rank);
                                break;

                            case TypeDefinition _:
                                break;

                            case TypeReference typeReference:
                                instruction.Operand = codeImporter.ImportTypeReference(typeReference);
                                break;

                            case FieldReference fieldReference:
                                fieldReference.FieldType = codeImporter.ImportTypeReference(fieldReference.FieldType);
                                break;
                        }
                    }
                }
            }

            var importedAssemblyNames = new HashSet<string>(codeImporter.ListImportedModules().Select(m => m.Assembly.FullName));

            module.AssemblyReferences.RemoveAll(ar => importedAssemblyNames.Contains(ar.FullName));
        }

        private static void MergeGenericParameters(CodeImporter codeImporter, IGenericParameterProvider provider)
        {
            if (provider?.HasGenericParameters != true)
                return;

            foreach (var parameter in provider.GenericParameters)
            {
                MergeTypes(codeImporter, parameter.Constraints);
            }
        }

        private static void MergeTypes(CodeImporter codeImporter, [NotNull] IList<TypeReference> types)
        {
            for (int i = 0; i < types.Count; i++)
            {
                types[i] = codeImporter.ImportTypeReference(types[i]);
            }
        }

        private static void MergeAttributes(CodeImporter codeImporter, ICustomAttributeProvider attributeProvider)
        {
            if (attributeProvider?.HasCustomAttributes != true)
                return;

            foreach (var attribute in attributeProvider.CustomAttributes)
            {
                attribute.Constructor.DeclaringType = codeImporter.ImportTypeReference(attribute.Constructor.DeclaringType);

                if (!attribute.HasConstructorArguments)
                    continue;

                for (var index = 0; index < attribute.ConstructorArguments.Count; index++)
                {
                    attribute.ConstructorArguments[index] = new CustomAttributeArgument(attribute.ConstructorArguments[index].Type, attribute.ConstructorArguments[index].Value);
                }
            }
        }

        public static void RemoveAll<T>([NotNull, ItemCanBeNull] this ICollection<T> target, [NotNull] Func<T, bool> condition)
        {
            target.RemoveAll(target.Where(condition).ToArray());
        }

        public static void RemoveAll<T>([NotNull, ItemCanBeNull] this ICollection<T> target, [NotNull, ItemCanBeNull] IEnumerable<T> items)
        {
            foreach (var i in items)
            {
                target.Remove(i);
            }
        }
    }

    internal interface IModuleResolver
    {
        [CanBeNull]
        ModuleDefinition Resolve([NotNull] TypeReference typeReference, [NotNull] string assemblyName);
    }

    internal class AssemblyModuleResolver : IModuleResolver
    {
        [NotNull]
        private readonly HashSet<string> _assemblyNames;

        public AssemblyModuleResolver([NotNull, ItemNotNull] params Assembly[] assemblies)
        {
            _assemblyNames = new HashSet<string>(assemblies.Select(a => a.FullName));
        }

        public ModuleDefinition Resolve(TypeReference typeReference, string assemblyName)
        {
            return _assemblyNames.Contains(assemblyName) ? typeReference.Resolve()?.Module : null;
        }
    }

    internal class LocalReferenceModuleResolver : IModuleResolver
    {
        [NotNull]
        private readonly HashSet<string> _ignoredAssemblyNames = new HashSet<string>();

        public ModuleDefinition Resolve(TypeReference typeReference, string assemblyName)
        {
            if (_ignoredAssemblyNames.Contains(assemblyName))
                return null;

            var module = typeReference.Resolve().Module;

            var moduleFileName = module.FileName;

            if (Path.GetDirectoryName(moduleFileName) == @".")
            {
                return module;
            }

            _ignoredAssemblyNames.Add(assemblyName);
            return null;
        }
    }
}