namespace FodyTools
{
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
}
