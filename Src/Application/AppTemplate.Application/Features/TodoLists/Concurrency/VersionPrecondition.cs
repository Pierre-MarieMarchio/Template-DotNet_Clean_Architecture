namespace AppTemplate.Application.Features.TodoLists.Concurrency;

/// <summary>
/// The aggregate versions a caller will accept before its change is applied.
/// </summary>
/// <remarks>
/// A command carrying one of these has been decided against a particular state of the aggregate,
/// and must not be applied to any other. The check belongs to the use case rather than to the
/// caller that built the command: only the use case holds the aggregate it loaded, so only there is
/// the comparison free of a window in which somebody else can commit.
/// <para>
/// An empty set is a precondition nothing can satisfy, which is the right answer for a caller that
/// named a validator this application could never have issued.
/// </para>
/// </remarks>
public sealed record VersionPrecondition(IReadOnlyList<uint> AcceptableVersions)
{
    public bool IsSatisfiedBy(uint version) => AcceptableVersions.Contains(version);
}
