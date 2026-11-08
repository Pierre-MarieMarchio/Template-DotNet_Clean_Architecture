namespace AppTemplate.Application.Core.Common.Concurrency;

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
