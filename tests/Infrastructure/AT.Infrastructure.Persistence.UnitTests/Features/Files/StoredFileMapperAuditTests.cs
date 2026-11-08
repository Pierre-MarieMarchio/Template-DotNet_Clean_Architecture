using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

public sealed class StoredFileMapperAuditTests
{
    private readonly StoredFileMapper _mapper = new();

    [Fact]
    public void ToAggregate_LeavesTheModificationStampsUnset_WhenTheRowWasNeverModified()
    {
        var record = AStoredRow();
        record.LastModifiedAt = null;
        record.LastModifiedBy = null;

        var aggregate = _mapper.ToAggregate(record);

        aggregate.LastModifiedAt.ShouldBeNull();
        aggregate.LastModifiedBy.ShouldBeNull();
        aggregate.CreatedAt.ShouldBe(AStoredFileAggregate.CreatedAt);
        aggregate.CreatedBy.ShouldBe(AStoredFileAggregate.CreatedBy);
    }

    [Fact]
    public void ToAggregate_CarriesBothModificationStamps_WhenTheRowWasModified()
    {
        var aggregate = _mapper.ToAggregate(AStoredRow());

        aggregate.LastModifiedAt.ShouldBe(AStoredFileAggregate.LastModifiedAt);
        aggregate.LastModifiedBy.ShouldBe(AStoredFileAggregate.LastModifiedBy);
    }

    [Fact]
    public void ToAggregate_RefusesARowWithAModifierButNoModificationTime()
    {
        var record = AStoredRow();
        record.LastModifiedAt = null;

        var failure = Should.Throw<InvalidOperationException>(() => _mapper.ToAggregate(record));

        failure.Message.ShouldContain(record.Id.ToString());
    }

    [Fact]
    public void ToAggregate_RefusesAnAvailableRowWithNoConfirmationInstant()
    {
        var record = AStoredRow();
        record.AvailableAt = null;

        Should.Throw<DomainException>(() => _mapper.ToAggregate(record));
    }

    private StoredFileRecord AStoredRow() => _mapper.ToNewRecord(AStoredFileAggregate.FullyPopulated());
}
