using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mappers;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

/// <summary>
/// How the audit stamps come off a row. The fidelity tests only ever load a fully stamped one, so the
/// shape every freshly inserted list actually has — created, never modified — is exercised here.
/// </summary>
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

    private TodoListRecord AStoredRow() => _mapper.ToNewRecord(ATodoListAggregate.FullyPopulated());
}
