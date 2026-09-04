namespace AppTemplate.Application.Common;

/// <summary>
/// Thrown by the persistence layer in place of its provider-specific concurrency exception, so the
/// transport can answer 409 without depending on whichever store detected the conflict.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message) : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConcurrencyConflictException()
    {
    }
}
