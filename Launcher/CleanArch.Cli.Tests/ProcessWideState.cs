using Xunit;

namespace CleanArch.Cli.Tests;

[CollectionDefinition(Name)]
public sealed class ProcessWideState
{
    private ProcessWideState()
    {
    }

    public const string Name = "The environment, the console, the current directory and ExternalCommand.Handler are process-wide";
}
