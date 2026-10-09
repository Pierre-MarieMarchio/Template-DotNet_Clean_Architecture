namespace AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;

/// <summary>
/// Thrown by <see cref="IOwnerQuotaLock"/> when the lock was held past the wait limit. Nothing was read or written.
/// </summary>
public sealed class OwnerQuotaLockTimeoutException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public OwnerQuotaLockTimeoutException()
        : base("The owner's quota lock was not obtained in time.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public OwnerQuotaLockTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and its cause.</summary>
    public OwnerQuotaLockTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
