using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Reminders;

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
