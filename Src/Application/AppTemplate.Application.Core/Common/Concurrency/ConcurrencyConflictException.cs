namespace AppTemplate.Application.Core.Common.Concurrency;

/// <summary>
/// Thrown by the persistence layer in place of its provider-specific concurrency exception, so the
/// transport can answer 409 without depending on whichever store detected the conflict.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    /// <summary>The usual form: the persistence layer says which write lost.</summary>
    public ConcurrencyConflictException(string message) : base(message)
    {
    }

    /// <summary>
    /// Keeps the provider's own exception for the log while the transport still sees only this type.
    /// </summary>
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Present only to satisfy the standard exception-constructor set (CA1032); a conflict this template
    /// raises always has something to say.
    /// </summary>
    public ConcurrencyConflictException()
    {
    }
}
