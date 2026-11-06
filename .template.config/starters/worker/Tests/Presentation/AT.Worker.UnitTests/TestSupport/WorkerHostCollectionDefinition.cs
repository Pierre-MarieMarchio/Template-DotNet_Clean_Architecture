using Xunit;

namespace AppTemplate.Worker.UnitTests.TestSupport;

[CollectionDefinition(Name, DisableParallelization = true)]
public static class WorkerHostCollectionDefinition
{
    public const string Name = "Worker host";
}
