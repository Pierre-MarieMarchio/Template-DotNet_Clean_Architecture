namespace AppTemplate.Application.Core.Common.Concurrency;

public sealed record VersionPrecondition(IReadOnlyList<uint> AcceptableVersions)
{
    public bool IsSatisfiedBy(uint version) => AcceptableVersions.Contains(version);
}
