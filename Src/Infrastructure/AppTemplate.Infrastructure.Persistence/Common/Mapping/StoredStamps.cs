using AppTemplate.Domain.Common.Abstractions;

namespace AppTemplate.Infrastructure.Persistence.Common.Mapping;

/// <summary>
/// The version and the audit stamps, read back onto an aggregate just rehydrated from its own row.
/// <para>
/// Every feature's <c>ToAggregate</c> ends the same way, because the store owns exactly the same four
/// values regardless of what the aggregate itself looks like: the concurrency token, and the
/// created/modified pair the audit interceptor writes. That is a genuine duplicate between
/// <c>TodoListMapper</c> and <c>ReminderMapper</c> — the two implementations differed only in the word
/// naming the aggregate in the one message a malformed row can raise — and collecting it here removes
/// the duplicate without touching the part of either mapper that actually needs a reviewer's eyes: the
/// field-by-field translation the round-trip fidelity test exists to check.
/// </para>
/// <para>
/// <b>A method, not a base class.</b> A base <c>Mapper&lt;TAggregate, TRecord&gt;</c> would have nothing
/// else to put in it — <c>ToNewRecord</c> and <c>WriteTo</c> are exactly where the two mappers stop
/// agreeing — and splitting one mapper's total, checkable behaviour across two files for the sake of a
/// four-line tail is what the round-trip test's reader would then have to read around rather than see
/// whole. See <c>docs/adr/0027</c>.
/// </para>
/// </summary>
internal static class StoredStamps
{
    /// <summary>
    /// Applies the version and audit stamps a row carries onto the aggregate just rebuilt from it.
    /// </summary>
    /// <param name="aggregate">The aggregate just rehydrated from <paramref name="record"/>'s own
    /// columns.</param>
    /// <param name="record">The row <paramref name="aggregate"/> was rehydrated from.</param>
    /// <param name="version">The row's concurrency token. It has no shared interface to read it through
    /// — unlike the audit stamps, it is not part of <see cref="IAuditable"/> — so it is passed separately
    /// rather than read off <paramref name="record"/> a second way.</param>
    /// <param name="recordId">The row's id, named in the exception a malformed pairing raises.</param>
    /// <param name="aggregateName">The word naming the aggregate in that same exception — "To-do list",
    /// "Reminder" — so the message reads like the rest of the mapper it came from.</param>
    internal static void ApplyTo<TAggregate>(
        TAggregate aggregate,
        IAuditable record,
        uint version,
        Guid recordId,
        string aggregateName)
        where TAggregate : IVersioned, IAuditable
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        aggregate.SetVersion(version);
        aggregate.SetCreated(record.CreatedAt, record.CreatedBy);

        if (record.LastModifiedAt is { } lastModifiedAt)
        {
            aggregate.SetLastModified(lastModifiedAt, record.LastModifiedBy);
        }
        else if (record.LastModifiedBy is not null)
        {
            // The two stamps move together, written by the audit interceptor and by nothing else, and
            // the domain has no state for "modified by somebody at no time". A row in that shape is
            // refused rather than half-read, which would drop the modifier without a sound.
            throw new InvalidOperationException(
                $"{aggregateName} '{recordId}' records a last modifier but no modification time. The "
                + "audit columns are written as a pair, so this row was changed by something else.");
        }
    }
}
