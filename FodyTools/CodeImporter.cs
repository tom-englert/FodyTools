namespace FodyTools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
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
        // ReSharper disable once AssignNullToNotNullAttribute
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
        private readonly IList<Action> _deferredActions = new List<Action>();

        private enum Priority
        {
            Instructions,
            Operands
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

        [NotNull]
        public Func<string, string> NamespaceDecorator { get; set; } = value => value;

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

            var targetType = Import(member.GetDeclaringType());
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

            var targetType = Import(member.GetDeclaringType());
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

            var targetType = Import(member.GetDeclaringType());
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
        private TypeDefinition ImportTypeDefinition([CanBeNull] TypeDefinition sourceType)
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

            targetType = new TypeDefinition(NamespaceDecorator(sourceType.Namespace), sourceType.Name, sourceType.Attributes)
            {
                ClassSize = sourceType.ClassSize,
                PackingSize = sourceType.PackingSize
            };

            _targetTypesBySourceName.Add(sourceType.FullName, targetType);
            _targetTypes.Add(targetType);

            targetType.DeclaringType = ImportTypeDefinition(sourceType.DeclaringType);

            CopyGenericParameters(sourceType, targetType);

            targetType.BaseType = InternalImportType(sourceType.BaseType, null);

            foreach (var sourceTypeInterface in sourceType.Interfaces)
            {
                var targetInterface = new InterfaceImplementation(InternalImportType(sourceTypeInterface.InterfaceType, null));
                CopyAttributes(sourceTypeInterface, targetInterface);
                targetType.Interfaces.Add(targetInterface);
            }

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
            CopyAttributes(sourceType, targetType);

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
                var targetDefinition = new PropertyDefinition(sourceDefinition.Name, sourceDefinition.Attributes, InternalImportType(sourceDefinition.PropertyType, null))
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
                var targetDefinition = new EventDefinition(sourceDefinition.Name, sourceDefinition.Attributes, InternalImportType(sourceDefinition.EventType, null))
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

                var targetDefinition = new FieldDefinition(fieldName, sourceDefinition.Attributes, InternalImportType(sourceDefinition.FieldType, null))
                {
                    InitialValue = sourceDefinition.InitialValue,
                    Offset = sourceDefinition.Offset
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

        [CanBeNull]
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
                ImplAttributes = sourceDefinition.ImplAttributes
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

            CopyReturnType(sourceDefinition, target);

            ImportMethodBody(sourceDefinition, target);

            return target;
        }

        private void CopyReturnType([NotNull] IMethodSignature source, [NotNull] MethodReference target)
        {
            var sourceReturnType = source.MethodReturnType;

            var targetReturnType = new MethodReturnType(target)
            {
                ReturnType = InternalImportType(source.ReturnType, target),
                MarshalInfo = sourceReturnType.MarshalInfo,
                Attributes = sourceReturnType.Attributes,
                Name = sourceReturnType.Name
            };

            if (sourceReturnType.HasConstant)
            {
                targetReturnType.Constant = sourceReturnType.Constant;
            }

            CopyAttributes(sourceReturnType, targetReturnType);

            target.MethodReturnType = targetReturnType;
        }

        private void CopyAttributes([NotNull] ICustomAttributeProvider source, [NotNull] ICustomAttributeProvider target)
        {
            if (!source.HasCustomAttributes)
                return;

            foreach (var sourceAttribute in source.CustomAttributes)
            {
                var attributeType = InternalImportType(sourceAttribute.AttributeType, null);

                var constructor = attributeType.Resolve()
                    .GetConstructors()
                    .Where(ctor => !ctor.IsStatic)
                    .Single(ctor => ctor.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(sourceAttribute.Constructor.Parameters.Select(p => p.ParameterType.FullName)));

                if (constructor == null)
                    continue;

                var targetAttribute = new CustomAttribute(TargetModule.ImportReference(constructor), sourceAttribute.GetBlob());
                if (sourceAttribute.HasConstructorArguments)
                {
                    targetAttribute.ConstructorArguments.AddRange(sourceAttribute.ConstructorArguments.Select(a => new CustomAttributeArgument(InternalImportType(a.Type, null), a.Value)));
                }

                target.CustomAttributes.Add(targetAttribute);
            }
        }

        private void ImportMethodBody([NotNull] MethodDefinition source, [NotNull] MethodDefinition target)
        {
            if (!source.HasBody)
                return;

            var sourceMethodBody = source.Body;
            var targetMethodBody = target.Body;

            targetMethodBody.InitLocals = sourceMethodBody.InitLocals;

            foreach (var sourceVariable in sourceMethodBody.Variables)
            {
                targetMethodBody.Variables.Add(new VariableDefinition(InternalImportType(sourceVariable.VariableType, target)));
            }

            ExecuteDeferred(Priority.Instructions, () => CopyInstructions(source, target));
        }

        private void CopyParameters([NotNull] IMethodSignature sourceMethod, [NotNull] MethodReference targetMethod)
        {
            foreach (var sourceParameter in sourceMethod.Parameters)
            {
                var targetParameter = new ParameterDefinition(sourceParameter.Name, sourceParameter.Attributes, InternalImportType(sourceParameter.ParameterType, targetMethod));

                CopyAttributes(sourceParameter, targetParameter);

                if (sourceParameter.HasMarshalInfo)
                {
                    targetParameter.MarshalInfo = sourceParameter.MarshalInfo;
                }

                if (sourceParameter.HasConstant)
                {
                    targetParameter.Constant = sourceParameter.Constant;
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
                var parameter = new GenericParameter(genericParameter.Name, InternalImportType(genericParameter.DeclaringType, null))
                {
                    Attributes = genericParameter.Attributes
                };

                if (genericParameter.HasConstraints)
                {
                    foreach (var constraint in genericParameter.Constraints)
                    {
                        parameter.Constraints.Add(InternalImportType(constraint.GetElementType(), null));
                    }
                }

                target.GenericParameters.Add(parameter);
            }
        }

        private void CopyGenericParameters([NotNull] MethodReference source, [NotNull] MethodReference target)
        {
            if (!source.HasGenericParameters)
                return;

            foreach (var genericParameter in source.GenericParameters)
            {
                var provider = genericParameter.Type == GenericParameterType.Method
                    ? (IGenericParameterProvider)target
                    : InternalImportType(genericParameter.DeclaringType, null);

                var parameter = new GenericParameter(genericParameter.Name, provider)
                {
                    Attributes = genericParameter.Attributes
                };

                if (genericParameter.HasConstraints)
                {
                    foreach (var constraint in genericParameter.Constraints)
                    {
                        parameter.Constraints.Add(InternalImportType(constraint.GetElementType(), target));
                    }
                }

                target.GenericParameters.Add(parameter);
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
                    targetHandler.CatchType = InternalImportType(sourceHandler.CatchType, null);
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

            if (sourceDebugInformation == null || true != targetDebugInformation?.HasSequencePoints)
                return;

            var scope = targetDebugInformation.Scope = new ScopeDebugInformation(targetInstructions.First(), targetInstructions.Last());

            foreach (var variable in sourceDebugInformation.Scope.Variables)
            {
                var targetVariable = targetBody.Variables[variable.Index];

                scope.Variables.Add(new VariableDebugInformation(targetVariable, variable.Name));
            }
        }

        [NotNull]
        [SuppressMessage("ReSharper", "ImplicitlyCapturedClosure")]
        private Instruction CloneInstruction([NotNull] Instruction source, [NotNull] MethodDefinition targetMethod, [NotNull] IReadOnlyDictionary<Instruction, Instruction> instructionMap)
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
                    targetInstruction.Operand = InternalImportType(typeReference, targetMethod);
                    break;

                case FieldReference fieldReference:
                    ExecuteDeferred(Priority.Operands, () => targetInstruction.Operand = new FieldReference(fieldReference.Name, InternalImportType(fieldReference.FieldType, targetMethod), InternalImportType(fieldReference.DeclaringType, targetMethod)));
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
                EndColumn = sequencePoint.EndColumn
            };
        }

        [ContractAnnotation("source:notnull=>notnull")]
        public TypeReference ImportType([CanBeNull] TypeReference source, [CanBeNull] MethodReference targetMethod)
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            return ProcessDeferredActions(InternalImportType(source, targetMethod));
        }

        [ContractAnnotation("source:notnull=>notnull")]
        private TypeReference InternalImportType([CanBeNull] TypeReference source, [CanBeNull] MethodReference targetMethod)
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
                    return new ByReferenceType(InternalImportType(byReferenceType.ElementType, targetMethod));

                case ArrayType arrayType:
                    return new ArrayType(InternalImportType(arrayType.ElementType, targetMethod), arrayType.Rank);

                case RequiredModifierType requiredModifierType:
                    return new RequiredModifierType(InternalImportType(requiredModifierType.ModifierType, targetMethod), InternalImportType(requiredModifierType.ElementType, targetMethod));

                default:
                    return ImportTypeReference(source, targetMethod);
            }
        }

        [NotNull]
        private TypeReference ImportTypeReference([NotNull] TypeReference source, [CanBeNull] MethodReference targetMethod)
        {
            Debug.Assert(source.GetType() == typeof(TypeReference));

            if (IsLocalOrExternalReference(source))
            {
                return TargetModule.ImportReference(source);
            }

            var typeDefinition = source.Resolve();

            if (typeDefinition == null)
                throw new InvalidOperationException($"Unable to resolve type {source}");

            return InternalImportType(typeDefinition, targetMethod);
        }

        [NotNull]
        private TypeReference ImportGenericInstanceType([NotNull] GenericInstanceType source, [CanBeNull] MethodReference targetMethod)
        {
            var target = new GenericInstanceType(InternalImportType(source.ElementType, targetMethod));

            foreach (var genericArgument in source.GenericArguments)
            {
                target.GenericArguments.Add(InternalImportType(genericArgument, targetMethod));
            }

            return target;
        }

        [NotNull]
        private TypeReference ImportGenericParameter([NotNull] GenericParameter source, [CanBeNull] MethodReference targetMethod)
        {
            var genericParameterProvider = (source.Type == GenericParameterType.Method)
                ? (targetMethod ?? throw new InvalidOperationException("Need a method reference for generic method parameter."))
                : (IGenericParameterProvider)InternalImportType(source.DeclaringType, targetMethod);

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

            target.DeclaringType = InternalImportType(source.DeclaringType, target);

            CopyReturnType(source, target);

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
                target.GenericArguments.Add(InternalImportType(genericArgument, target));
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

            if (module == null)
                return false;

            RegisterSourceModule(module);
            return true;

        }

        private void ExecuteDeferred(Priority priority, [NotNull] Action action)
        {
            switch (priority)
            {
                case Priority.Instructions:
                    _deferredActions.Insert(0, action);
                    break;

                default:
                    _deferredActions.Add(action);
                    break;
            }
        }

        [NotNull]
        private TypeReference TemporaryPlaceholderType => new TypeReference("temporary", "type", TargetModule, TargetModule);

        [ContractAnnotation("target:notnull=>notnull")]
        private T ProcessDeferredActions<T>(T target)
            where T : class
        {
            while (true)
            {
                var action = _deferredActions.FirstOrDefault();
                if (action == null)
                    break;

                _deferredActions.RemoveAt(0);
                action();
            }

            return target;
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

                typeDefinition.BaseType = codeImporter.ImportType(typeDefinition.BaseType, null);

                foreach (var interfaceImplementation in typeDefinition.Interfaces)
                {
                    MergeAttributes(codeImporter, interfaceImplementation);
                    interfaceImplementation.InterfaceType = codeImporter.ImportType(interfaceImplementation.InterfaceType, null);
                }

                foreach (var fieldDefinition in typeDefinition.Fields)
                {
                    MergeAttributes(codeImporter, fieldDefinition);
                    fieldDefinition.FieldType = codeImporter.ImportType(fieldDefinition.FieldType, null);
                }

                foreach (var eventDefinition in typeDefinition.Events)
                {
                    MergeAttributes(codeImporter, eventDefinition);
                    eventDefinition.EventType = codeImporter.ImportType(eventDefinition.EventType, null);
                }

                foreach (var propertyDefinition in typeDefinition.Properties)
                {
                    MergeAttributes(codeImporter, propertyDefinition);

                    propertyDefinition.PropertyType = codeImporter.ImportType(propertyDefinition.PropertyType, null);

                    if (!propertyDefinition.HasParameters)
                        continue;

                    foreach (var parameter in propertyDefinition.Parameters)
                    {
                        MergeAttributes(codeImporter, parameter);
                        parameter.ParameterType = codeImporter.ImportType(parameter.ParameterType, null);
                    }
                }

                foreach (var methodDefinition in typeDefinition.Methods)
                {
                    MergeAttributes(codeImporter, methodDefinition);
                    MergeGenericParameters(codeImporter, methodDefinition);

                    methodDefinition.ReturnType = codeImporter.ImportType(methodDefinition.ReturnType, methodDefinition);

                    var methodOverrides = methodDefinition.Overrides;

                    for (var i = 0; i < methodOverrides.Count; i++)
                    {
                        var methodOverride = methodOverrides[i];

                        if (methodOverride is MethodDefinition)
                            throw new NotImplementedException("Method overrides using MethodDefinition is not yet supported");

                        var returnType = codeImporter.ImportType(methodOverride.ReturnType, methodDefinition);
                        var declaringType = codeImporter.ImportType(methodOverride.DeclaringType, methodDefinition);

                        var newOverride = new MethodReference(methodOverride.Name, returnType, declaringType)
                        {
                            CallingConvention = methodOverride.CallingConvention,
                            ExplicitThis = methodOverride.ExplicitThis,
                            HasThis = methodOverride.HasThis,
                            MetadataToken = methodOverride.MetadataToken,
                        };

                        if (methodOverride.HasParameters)
                        {
                            newOverride.Parameters.AddRange(methodOverride.Parameters);
                            foreach (var parameter in newOverride.Parameters)
                            {
                                MergeAttributes(codeImporter, parameter);
                                parameter.ParameterType = codeImporter.ImportType(parameter.ParameterType, methodDefinition);
                            }
                        }

                        if (methodOverride.HasGenericParameters)
                        {
                            newOverride.GenericParameters.AddRange(methodOverride.GenericParameters);
                            MergeGenericParameters(codeImporter, newOverride);
                        }

                        methodOverrides[i] = newOverride;
                    }

                    foreach (var parameter in methodDefinition.Parameters)
                    {
                        MergeAttributes(codeImporter, parameter);
                        parameter.ParameterType = codeImporter.ImportType(parameter.ParameterType, methodDefinition);
                    }

                    var methodBody = methodDefinition.Body;
                    if (methodBody == null)
                        continue;

                    foreach (var variable in methodBody.Variables)
                    {
                        variable.VariableType = codeImporter.ImportType(variable.VariableType, methodDefinition);
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
                                methodReference.DeclaringType = codeImporter.ImportType(methodReference.DeclaringType, methodDefinition);
                                methodReference.ReturnType = codeImporter.ImportType(methodReference.ReturnType, methodDefinition);
                                foreach (var parameter in methodReference.Parameters)
                                {
                                    parameter.ParameterType = codeImporter.ImportType(parameter.ParameterType, methodDefinition);
                                }
                                break;

                            case TypeDefinition _:
                                break;

                            case TypeReference typeReference:
                                instruction.Operand = codeImporter.ImportType(typeReference, methodDefinition);
                                break;

                            case FieldReference fieldReference:
                                fieldReference.FieldType = codeImporter.ImportType(fieldReference.FieldType, methodDefinition);
                                fieldReference.DeclaringType = codeImporter.ImportType(fieldReference.DeclaringType, methodDefinition);
                                break;
                        }
                    }
                }
            }

            var importedAssemblyNames = new HashSet<string>(codeImporter.ListImportedModules().Select(m => m.Assembly.FullName));

            module.AssemblyReferences.RemoveAll(ar => importedAssemblyNames.Contains(ar.FullName));
        }

        private static void MergeGenericParameters([NotNull] CodeImporter codeImporter, [CanBeNull] IGenericParameterProvider provider)
        {
            if (provider?.HasGenericParameters != true)
                return;

            foreach (var parameter in provider.GenericParameters)
            {
                MergeTypes(codeImporter, parameter.Constraints);
            }
        }

        private static void MergeTypes([NotNull] CodeImporter codeImporter, [NotNull] IList<TypeReference> types)
        {
            for (var i = 0; i < types.Count; i++)
            {
                types[i] = codeImporter.ImportType(types[i], null);
            }
        }

        private static void MergeAttributes([NotNull] CodeImporter codeImporter, [CanBeNull] ICustomAttributeProvider attributeProvider)
        {
            if (attributeProvider?.HasCustomAttributes != true)
                return;

            foreach (var attribute in attributeProvider.CustomAttributes)
            {
                attribute.Constructor.DeclaringType = codeImporter.ImportType(attribute.Constructor.DeclaringType, null);

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

        public AssemblyModuleResolver([NotNull, ItemNotNull] params string[] assemblyNames)
        {
            _assemblyNames = new HashSet<string>(assemblyNames);
        }

        [CanBeNull]
        public ModuleDefinition Resolve(TypeReference typeReference, string assemblyName)
        {
            return _assemblyNames.Contains(assemblyName) ? typeReference.Resolve()?.Module : null;
        }
    }

    internal class LocalReferenceModuleResolver : IModuleResolver
    {
        [NotNull]
        private readonly HashSet<string> _ignoredAssemblyNames = new HashSet<string>();

        [CanBeNull]
        public ModuleDefinition Resolve(TypeReference typeReference, string assemblyName)
        {
            if (_ignoredAssemblyNames.Contains(assemblyName))
                return null;

            try
            {
                var module = typeReference.Resolve().Module;
                var moduleFileName = module.FileName;

                if (Path.GetDirectoryName(moduleFileName) == @".")
                {
                    return module;
                }
            }
            catch (AssemblyResolutionException)
            {
            }

            _ignoredAssemblyNames.Add(assemblyName);
            return null;
        }
    }
}