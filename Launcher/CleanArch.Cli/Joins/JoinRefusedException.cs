namespace CleanArch.Cli.Joins;

public sealed class JoinRefusedException : Exception
{
    public JoinRefusedException()
    {
    }

    public JoinRefusedException(string message)
        : base(message)
    {
    }

    public JoinRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
