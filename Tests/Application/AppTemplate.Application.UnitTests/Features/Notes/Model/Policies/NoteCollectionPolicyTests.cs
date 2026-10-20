using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Features.Notes.Model.Policies;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Notes.Model.Policies;

public sealed class NoteCollectionPolicyTests
{
    private static readonly NoteCollectionPolicy _policy = NoteCollectionPolicy.Instance;

    [Fact]
    public void DefaultSort_ParsesAgainstItsOwnWhitelist() =>
        SortOrder.Parse(null, _policy).IsSuccess.ShouldBeTrue();

    [Fact]
    public void EverySortableField_SupportsKeysetPaging() =>
        _policy.SortableFields.ShouldAllBe(field => field.SupportsKeyset);

    [Fact]
    public void TheDefaultPageSize_FitsUnderTheCeiling() =>
        _policy.DefaultPageSize.ShouldBeLessThanOrEqualTo(_policy.MaxPageSize);
}
