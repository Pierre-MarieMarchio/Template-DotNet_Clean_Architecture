namespace CA.Domain.Modules.Auth.Exceptions;

public class RegistrationException : Exception
{
    public RegistrationException(string? message) : base(message)
    {
    }
}
