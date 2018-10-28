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
    /// A generic logger interface to decouple implementation.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Logs a debug message.
        /// </summary>
        /// <param name="message">The message.</param>
        void LogDebug([NotNull] string message);
        /// <summary>
        /// Logs an info message.
        /// </summary>
        /// <param name="message">The message.</param>
        void LogInfo([NotNull] string message);
        /// <summary>
        /// Logs a warning.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sequencePoint">The optional sequence point where the problem occurred.</param>
        void LogWarning([NotNull] string message, SequencePoint sequencePoint = null);
        /// <summary>
        /// Logs an error.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sequencePoint">The optional sequence point where the error occurred.</param>
        void LogError([NotNull] string message, SequencePoint sequencePoint = null);
    }

    /// <summary>
    /// A generic type system interface to decouple implementation.
    /// </summary>
    public interface ITypeSystem
    {
        /// <summary>
        /// Finds the type in the assemblies that the weaver has registered for scanning.
        /// </summary>
        /// <param name="typeName">Name of the type.</param>
        /// <returns>The type definition</returns>
        [NotNull]
        TypeDefinition FindType([NotNull] string typeName);

        /// <summary>
        /// Finds the type in the assemblies that the weaver has registered for scanning.
        /// </summary>
        /// <param name="typeName">Name of the type.</param>
        /// <param name="value">The return value.</param>
        /// <returns><c>true</c> if the type was found and value contains a valid item.</returns>
        [ContractAnnotation("value:null => false")]
        bool TryFindType([NotNull] string typeName, out TypeDefinition value);
    }

    /// <summary>
    /// A <see cref="Fody.BaseModuleWeaver" /> implementing <see cref="ILogger"/> and <see cref="ITypeSystem"/>, decorated with <see cref="NotNullAttribute"/>.
    /// </summary>
    /// <seealso cref="Fody.BaseModuleWeaver" />
    /// <seealso cref="FodyTools.ILogger" />
    /// <seealso cref="FodyTools.ITypeSystem" />
    [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
    [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
    [ExcludeFromCodeCoverage]
    public abstract class AbstractModuleWeaver : BaseModuleWeaver, ILogger, ITypeSystem
    {
        /// <summary>
        /// An instance of <see cref="T:Mono.Cecil.ModuleDefinition" /> for processing.
        /// </summary>
        [NotNull]
        protected new ModuleDefinition ModuleDefinition => base.ModuleDefinition;

        /// <summary>
        /// The full element XML from FodyWeavers.xml.
        /// </summary>
        [NotNull]
        protected new XElement Config => base.Config;

        /// <summary>
        /// Commonly used <see cref="T:Mono.Cecil.TypeReference" />s.
        /// </summary>
        [NotNull]
        protected new Fody.TypeSystem TypeSystem => base.TypeSystem;

        /// <summary>
        /// The full path of the target assembly.
        /// </summary>
        [NotNull]
        protected new string AssemblyFilePath => base.AssemblyFilePath;

        /// <summary>
        /// The full directory path of the target project.
        /// A copy of $(ProjectDir).
        /// </summary>
        [NotNull]
        protected new string ProjectDirectoryPath => base.ProjectDirectoryPath;

        /// <summary>
        /// The full directory path of the current weaver.
        /// </summary>
        [NotNull]
        protected new string AddinDirectoryPath => base.AddinDirectoryPath;

        /// <summary>
        /// The full directory path of the current solution.
        /// A copy of `$(SolutionDir)` or, if it does not exist, a copy of `$(MSBuildProjectDirectory)..\..\..\`. OPTIONAL
        /// </summary>
        [NotNull]
        protected new string SolutionDirectoryPath => base.SolutionDirectoryPath;

        /// <summary>
        /// A semicolon delimited string that contains
        /// all the references for the target project.
        /// A copy of the contents of the @(ReferencePath).
        /// </summary>
        [NotNull]
        protected new string References => base.References;

        /// <summary>
        /// A list of all the references marked as copy-local.
        /// A copy of the contents of the @(ReferenceCopyLocalPaths).
        /// </summary>
        /// <remarks>
        /// This list will be actively synced back to the build system, i.e. adding or removing items from this list will modify the @(ReferenceCopyLocalPaths) list of the current build.
        /// </remarks>
        [NotNull]
        protected new IList<string> ReferenceCopyLocalPaths => base.ReferenceCopyLocalPaths;

        /// <summary>
        /// A list of all the msbuild constants.
        /// A copy of the contents of the $(DefineConstants).
        /// </summary>
        [NotNull]
        protected new IList<string> DefineConstants => base.DefineConstants;

        void ILogger.LogDebug(string message)
        {
            LogMessage(message, MessageImportance.Low);
        }

        void ILogger.LogInfo(string message)
        {
            LogMessage(message, MessageImportance.Normal);
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
