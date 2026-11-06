namespace CleanArch.Cli;

public sealed class ProjectFileException : Exception
{
    public ProjectFileException()
    {
    }

    public ProjectFileException(string message)
        : base(message)
    {
    }

    public ProjectFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
