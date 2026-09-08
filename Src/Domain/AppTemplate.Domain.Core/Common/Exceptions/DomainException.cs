namespace AppTemplate.Domain.Core.Common.Exceptions;

/// <summary>
/// A domain invariant was violated: a caller tried to drive an aggregate into a state the
/// model forbids, which is a bug rather than a user error. Expected, user-facing failures
/// (not found, conflict, validation) are returned as <c>Result</c> values, not thrown.
/// </summary>
public sealed class DomainException : Exception
{
    /// <summary>Reports a violated invariant.</summary>
    /// <param name="message">Which invariant was violated. It is the only thing that tells a
    /// catch site which one, so there is deliberately no parameterless constructor.</param>
    public DomainException(string message) : base(message)
    {
    }

    /// <summary>Reports a violated invariant that a lower-level failure caused.</summary>
    /// <param name="message">Which invariant was violated.</param>
    /// <param name="innerException">The failure underneath it.</param>
    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
