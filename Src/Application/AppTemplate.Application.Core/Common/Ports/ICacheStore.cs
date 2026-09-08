namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// Keeps the answer to a read that is expensive to repeat and cheap to be slightly out of date.
/// </summary>
/// <remarks>
/// <para>
/// <b>What may be cached, and it is a short list.</b> An answer whose staleness is harmless: nothing
/// that decides an authorisation, enforces a bound, becomes an <c>ETag</c>, or tells something else
/// what to delete. A cached bound is a bound multiplied by however many processes hold a copy, and a
/// cached version is a precondition that no longer describes the thing it guards.
/// </para>
/// <para>
/// An entry may vanish before its lifetime is over, and a caller must be correct when it does: this
/// port promises to be faster, never to remember.
/// </para>
/// </remarks>
public interface ICacheStore
{
    /// <summary>
    /// The value under <paramref name="key"/>, computing and keeping it for
    /// <paramref name="lifetime"/> when it is not there.
    /// </summary>
    /// <param name="key">Identifies the answer, and everything the answer depends on.</param>
    /// <param name="factory">Computes the value when the entry is absent.</param>
    /// <param name="lifetime">How long the value may be handed back without being recomputed.</param>
    /// <param name="cancellationToken">Cancels both the lookup and the factory.</param>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the entry under <paramref name="key"/>, so the next read recomputes it. Called by
    /// whatever changed the answer.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
