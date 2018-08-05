namespace FodyTools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
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
    public class CodeImporter
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
        /// Imports the specified types.
        /// </summary>
        /// <param name="types">The types to import.</param>
        /// <returns>
        /// The list of all imported types in the target module, containing the specified types and all references.
        /// </returns>
        [NotNull]
        public IEnumerable<TypeDefinition> Import([NotNull, ItemNotNull] params Type[] types)
        {
            foreach (var type in types)
            {
                ImportType(type);
            }

            while (true)
            {
                var action = _deferredActions.OrderBy(a => (int)a.Priority).FirstOrDefault();
                if (action == null)
                    break;

                _deferredActions.Remove(action);
                action.Action();
            }

            return _targetTypes.Values.Where(t => t.DeclaringType == null);
        }

        private void ImportType([NotNull] Type type)
        {
            var assembly = type.Assembly;

            if (!_sourceModuleDefinitions.TryGetValue(assembly, out var sourceModule))
            {
                sourceModule = ModuleDefinition.ReadModule(assembly.Location);
                _sourceModuleDefinitions.Add(assembly, sourceModule);
            }

            var sourceType = sourceModule.GetType(type.FullName);

            ImportTypeDefinition(sourceType);
        }

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

            CopyGenericParameters(sourceType, targetType);

            targetType.BaseType = ImportType(sourceType.BaseType, null);

            CopyAttributes(sourceType, targetType);

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

            target = new MethodDefinition(source.Name, source.Attributes, VoidType);

            CopyAttributes(source, target);

            _targetMethods.Add(source, target);

            CopyGenericParameters(source, target);
            CopyParameters(source, target);

            targetType.Methods.Add(target);

            if (source.IsPInvokeImpl)
            {
                var moduleRef = _targetModule.ModuleReferences.FirstOrDefault(mr => mr.Name == source.PInvokeInfo.Module.Name);
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

                    var parameter = new GenericParameter(genericParameter.Name, provider);

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
            foreach (var sourceInstruction in source.Body.Instructions)
            {
                target.Body.Instructions.Add(CloneInstruction(sourceInstruction, target));
            }

            CopyExceptionHandlers(source.Body, target.Body);
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
                    ExecuteDeferred(Priority.Operands, () =>
                        targetInstruction.Operand = new FieldReference(fieldReference.Name, ImportType(fieldReference.FieldType, targetMethod), ImportType(fieldReference.DeclaringType, targetMethod)));
                    break;
            }

            return targetInstruction;
        }

        private TypeReference ImportType([NotNull] TypeReference source, MethodReference targetMethod)
        {
            switch (source)
            {
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

            return genericParameterProvider.GenericParameters[source.Position];
        }

        [NotNull]
        private MethodReference ImportMethodReference([NotNull] MethodReference source)
        {
            Debug.Assert(source.GetType() == typeof(MethodReference));

            var target = new MethodReference(source.Name, VoidType)
            {
                HasThis = source.HasThis,
                ExplicitThis = source.ExplicitThis,
                CallingConvention = source.CallingConvention
            };

            target.DeclaringType = ImportType(source.DeclaringType, target);

            CopyGenericParameters(source, target);
            CopyParameters(source, target);

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
        private TypeReference VoidType => _targetModule.TypeSystem.Void;

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
