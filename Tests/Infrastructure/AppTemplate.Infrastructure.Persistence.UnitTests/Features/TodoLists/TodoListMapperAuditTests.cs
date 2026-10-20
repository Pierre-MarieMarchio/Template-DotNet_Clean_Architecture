using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

public sealed class TodoListMapperAuditTests
{
    private readonly TodoListMapper _mapper = new();

    [Fact]
    public void ToAggregate_LeavesTheModificationStampsUnset_WhenTheRowWasNeverModified()
    {
        var record = AStoredRow();
        record.LastModifiedAt = null;
        record.LastModifiedBy = null;

        var aggregate = _mapper.ToAggregate(record);

        aggregate.LastModifiedAt.ShouldBeNull();
        aggregate.LastModifiedBy.ShouldBeNull();
        aggregate.CreatedAt.ShouldBe(ATodoListAggregate.CreatedAt);
        aggregate.CreatedBy.ShouldBe(ATodoListAggregate.CreatedBy);
    }

    [Fact]
    public void ToAggregate_CarriesBothModificationStamps_WhenTheRowWasModified()
    {
        var aggregate = _mapper.ToAggregate(AStoredRow());

        aggregate.LastModifiedAt.ShouldBe(ATodoListAggregate.LastModifiedAt);
        aggregate.LastModifiedBy.ShouldBe(ATodoListAggregate.LastModifiedBy);
    }

    [Fact]
    public void ToAggregate_RefusesARowWithAModifierButNoModificationTime()
    {
        var record = AStoredRow();
        record.LastModifiedAt = null;

        var failure = Should.Throw<InvalidOperationException>(() => _mapper.ToAggregate(record));

        failure.Message.ShouldContain(record.Id.ToString());
    }

    private TodoListRecord AStoredRow() => _mapper.ToNewRecord(ATodoListAggregate.FullyPopulated());
}
