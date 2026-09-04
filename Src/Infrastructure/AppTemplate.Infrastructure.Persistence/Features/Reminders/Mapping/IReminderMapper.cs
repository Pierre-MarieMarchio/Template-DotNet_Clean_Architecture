using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Mapping;

/// <summary>
/// Translates between the <see cref="Reminder"/> aggregate and the row that stores it.
/// <para>
/// A mapper that forgets a property throws nothing, logs nothing and fails no test that was not written
/// for it — it silently loses data, and the loss surfaces later as a value that "reset itself". A
/// reflection-driven round-trip test enumerates the aggregate's state and fails when a property does not
/// survive aggregate → record → aggregate, so the guarantee does not rest on a reviewer noticing.
/// </para>
/// </summary>
internal interface IReminderMapper
{
    /// <summary>Rebuilds an aggregate from a row.</summary>
    Reminder ToAggregate(ReminderRecord record);

    /// <summary>Builds the row for an aggregate that has never been stored.</summary>
    ReminderRecord ToNewRecord(Reminder aggregate);

    /// <summary>
    /// Writes the aggregate's current state onto an already-tracked row and lets EF's own diff decide
    /// what to write. Returns nothing: a reminder has no child collection whose reconciliation could
    /// leave the root looking unchanged, so there is nothing for a caller to act on beyond what EF's
    /// change tracker already sees.
    /// </summary>
    void WriteTo(Reminder aggregate, ReminderRecord record);
}
