using Xunit;

namespace CleanArch.Cli.Tests;

[CollectionDefinition(Name)]
public sealed class ProcessWideState
{
    private ProcessWideState()
    {
    }

    public const string Name = "The environment variables, the console and the current directory are process-wide";
}
