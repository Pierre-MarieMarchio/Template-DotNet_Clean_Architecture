namespace AppTemplate.Domain.Core.Common.Abstractions;

/// <summary>Opt-in optimistic-concurrency token, written by the persistence layer: getter to
/// everyone, setter only through this explicitly implemented interface, so forging a version
/// requires a visible cast. The domain carries it because a repository that maps onto a separate
/// persistence record must put the loaded version into the <c>WHERE</c> clause of the next write,
/// and can only do that if the aggregate remembers it.</summary>
public interface IVersioned
{
    /// <summary>
    /// The version the aggregate was loaded at, or last written at; zero when never persisted.
    /// </summary>
    uint Version { get; }

    /// <summary>Called by the persistence layer on load and again after a successful write.</summary>
    void SetVersion(uint version);
}
