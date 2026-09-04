namespace AppTemplate.Domain.Common.Abstractions;

/// <summary>
/// Opt-in optimistic-concurrency token, written by the persistence layer.
/// <para>
/// It exists for the same reason <see cref="IAuditable"/> does, and is shaped the same way: the
/// value is owned by the store, so the aggregate exposes a getter to everyone and a setter only
/// through an explicitly implemented interface. Application code that wanted to forge a version
/// would have to cast to this interface first, which is a visible act rather than an assignment
/// that reads like any other.
/// </para>
/// <para>
/// The domain carries the token — rather than leaving it entirely inside the persistence model —
/// because the token is part of the aggregate's identity in time: "the version of this list I made
/// my decision against". A repository that maps an aggregate onto a separate persistence record
/// must be able to put that value back into the <c>WHERE</c> clause of the next write, and it can
/// only do that if the aggregate it was handed remembers it.
/// </para>
/// </summary>
public interface IVersioned
{
    /// <summary>
    /// The version the aggregate was loaded at, or the version it was last written at. Zero for an
    /// aggregate that has never been persisted.
    /// </summary>
    uint Version { get; }

    /// <summary>Called by the persistence layer on load and again after a successful write.</summary>
    void SetVersion(uint version);
}
