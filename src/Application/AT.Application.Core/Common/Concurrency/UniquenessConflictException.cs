namespace AppTemplate.Application.Core.Common.Concurrency;

public sealed class UniquenessConflictException : Exception
{
    public UniquenessConflictException(string message) : base(message)
    {
    }

    public UniquenessConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UniquenessConflictException()
    {
    }
}
