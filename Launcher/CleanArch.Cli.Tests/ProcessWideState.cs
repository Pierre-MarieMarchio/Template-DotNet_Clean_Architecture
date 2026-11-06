using Xunit;

namespace CleanArch.Cli.Tests;

[CollectionDefinition(Name)]
public sealed class ProcessWideState
{
    public const string Name = "PATH and ExternalCommand.Handler are process-wide";
}
