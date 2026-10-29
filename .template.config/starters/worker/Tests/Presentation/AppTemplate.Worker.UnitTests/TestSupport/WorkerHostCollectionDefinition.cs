using Xunit;

namespace AppTemplate.Worker.UnitTests.TestSupport;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerHostCollectionDefinition
{
    public const string Name = "Worker host";
}
