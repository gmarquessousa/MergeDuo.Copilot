namespace MergeDuo.Copilot.Domain.Exceptions;

public abstract class CopilotException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class CopilotBadRequestException(string code, string message) : CopilotException(code, message);

public sealed class CopilotConfigurationException(string code, string message) : CopilotException(code, message);

public sealed class CopilotDependencyException(string code, string message, Exception? innerException = null)
    : CopilotException(code, message)
{
    public Exception? InnerDependencyException { get; } = innerException;
}
