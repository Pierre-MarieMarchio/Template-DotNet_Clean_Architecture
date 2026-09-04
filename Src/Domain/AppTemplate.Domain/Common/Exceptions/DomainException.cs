namespace AppTemplate.Domain.Common.Exceptions;

/// <summary>
/// A domain invariant was violated: a caller tried to drive an aggregate into a state the
/// model forbids, which is a bug rather than a user error. Expected, user-facing failures
/// (not found, conflict, validation) are returned as <c>Result</c> values, not thrown.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
