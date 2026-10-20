namespace AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;

public sealed class OwnerQuotaLockTimeoutException : Exception
{
    public OwnerQuotaLockTimeoutException()
        : base("The owner's quota lock was not obtained in time.")
    {
    }

    public OwnerQuotaLockTimeoutException(string message)
        : base(message)
    {
    }

    public OwnerQuotaLockTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
