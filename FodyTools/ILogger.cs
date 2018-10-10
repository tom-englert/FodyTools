namespace FodyTools
{
    using System.Diagnostics.CodeAnalysis;

    using Fody;

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

    [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
    public abstract class LoggingBaseModuleWeaver : BaseModuleWeaver, ILogger
    {
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
    }
}
