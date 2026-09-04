using AppTemplate.Infrastructure.Persistence.Features.Reminders.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Reminders;

/// <summary>
/// How the audit stamps come off a row. The fidelity tests only ever load a fully stamped one, so the
/// shape every freshly inserted reminder actually has — created, never modified — is exercised here.
/// </summary>
public sealed class ReminderMapperAuditTests
{
    private readonly ReminderMapper _mapper = new();

    [Fact]
    public void ToAggregate_LeavesTheModificationStampsUnset_WhenTheRowWasNeverModified()
    {
        var record = AStoredRow();
        record.LastModifiedAt = null;
        record.LastModifiedBy = null;

        var aggregate = _mapper.ToAggregate(record);

        aggregate.LastModifiedAt.ShouldBeNull();
        aggregate.LastModifiedBy.ShouldBeNull();
        aggregate.CreatedAt.ShouldBe(AReminderAggregate.CreatedAt);
        aggregate.CreatedBy.ShouldBe(AReminderAggregate.CreatedBy);
    }

    [Fact]
    public void ToAggregate_CarriesBothModificationStamps_WhenTheRowWasModified()
    {
        var aggregate = _mapper.ToAggregate(AStoredRow());

        aggregate.LastModifiedAt.ShouldBe(AReminderAggregate.LastModifiedAt);
        aggregate.LastModifiedBy.ShouldBe(AReminderAggregate.LastModifiedBy);
    }

    /// <summary>
    /// The stamps are written as a pair by one interceptor, so a row carrying a modifier and no
    /// timestamp cannot have come from this application. Reading the timestamp and ignoring the modifier
    /// would drop it without a sound, which is why the mismatch is refused instead.
    /// </summary>
    [Fact]
    public void ToAggregate_RefusesARowWithAModifierButNoModificationTime()
    {
        var record = AStoredRow();
        record.LastModifiedAt = null;

        var failure = Should.Throw<InvalidOperationException>(() => _mapper.ToAggregate(record));

        failure.Message.ShouldContain(record.Id.ToString());
    }

    private ReminderRecord AStoredRow() => _mapper.ToNewRecord(AReminderAggregate.FullyPopulated());
}
