namespace FodyTools
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Xml.Linq;

    using Fody;

    using JetBrains.Annotations;

    using Mono.Cecil;
    using Mono.Cecil.Cil;

    /// <summary>
    /// A generic logger interface to decouple implementation
    /// </summary>
    public interface ILogger
    {
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message, SequencePoint sequencePoint = null);
        void LogError(string message, SequencePoint sequencePoint = null);
    }

    public interface ITypeSystem
    {
        TypeDefinition FindType(string typeName);
        bool TryFindType(string typeName, out TypeDefinition value);
    }

    [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
    [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
    [ExcludeFromCodeCoverage]
    public abstract class AbstractModuleWeaver : BaseModuleWeaver, ILogger, ITypeSystem
    {
        [NotNull]
        protected new ModuleDefinition ModuleDefinition => base.ModuleDefinition;

        [NotNull]
        protected new XElement Config => base.Config;

        [NotNull]
        protected new Fody.TypeSystem TypeSystem => base.TypeSystem;

        [NotNull]
        protected new string AssemblyFilePath => base.AssemblyFilePath;

        [NotNull]
        protected new string ProjectDirectoryPath => base.ProjectDirectoryPath;

        [NotNull]
        protected new string AddinDirectoryPath => base.AddinDirectoryPath;

        [NotNull]
        protected new string SolutionDirectoryPath => base.SolutionDirectoryPath;

        [NotNull]
        protected new string References => base.References;

        [NotNull]
        protected new IList<string> ReferenceCopyLocalPaths => base.ReferenceCopyLocalPaths;

        [NotNull]
        protected new IList<string> DefineConstants => base.DefineConstants;

        void ILogger.LogDebug(string message)
        {
            LogDebug(message);
        }

        void ILogger.LogInfo(string message)
        {
            LogInfo(message);
        }

        void ILogger.LogWarning(string message, SequencePoint sequencePoint)
        {
            if (sequencePoint == null)
            {
                LogWarning(message);
            }
            else
            {
                LogWarningPoint(message, sequencePoint);
            }
        }

        void ILogger.LogError(string message, SequencePoint sequencePoint)
        {
            if (sequencePoint == null)
            {
                LogError(message);
            }
            else
            {
                LogErrorPoint(message, sequencePoint);
            }
        }

        TypeDefinition ITypeSystem.FindType(string typeName)
        {
            return FindType(typeName);
        }

        bool ITypeSystem.TryFindType(string typeName, out TypeDefinition value)
        {
            return TryFindType(typeName, out value);
        }
    }
}
