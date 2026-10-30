namespace CleanArch.Tooling;

public sealed class LauncherException : Exception
{
    public LauncherException()
    {
    }

    public LauncherException(string message)
        : base(message)
    {
    }

    public LauncherException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
