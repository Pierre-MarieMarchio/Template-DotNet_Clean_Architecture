using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

/// <summary>
/// How the audit stamps come off a row. The fidelity tests only ever load a fully stamped one, so the
/// shape every freshly registered file actually has — created, never modified — is exercised here.
/// </summary>
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

    /// <summary>
    /// The state and the confirmation instant are one fact recorded twice, and
    /// <c>StoredFile.Rehydrate</c> refuses a row where they disagree. The mapper has to let that
    /// refusal through rather than paper over it: a row loaded despite the contradiction becomes a file
    /// that can never be confirmed, or one served without its bytes ever having been checked.
    /// </summary>
    [Fact]
    public void ToAggregate_RefusesAnAvailableRowWithNoConfirmationInstant()
    {
        var record = AStoredRow();
        record.AvailableAt = null;

        Should.Throw<DomainException>(() => _mapper.ToAggregate(record));
    }

    private StoredFileRecord AStoredRow() => _mapper.ToNewRecord(AStoredFileAggregate.FullyPopulated());
}
