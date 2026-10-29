namespace AppTemplate.PresentationApp.Core.Common.Sessions;

public sealed class SignInFailedException : Exception
{
    public SignInFailedException()
    {
    }

    public SignInFailedException(string message)
        : base(message)
    {
    }

    public SignInFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
