using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Features.Notes.Policies;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Notes.Policies;

public sealed class NoteCollectionPolicyTests
{
    private static readonly NoteCollectionPolicy _policy = NoteCollectionPolicy.Instance;

    /// <summary>Its own default is whitelisted by the code a caller's <c>sort</c> takes, so a typo fails here.</summary>
    [Fact]
    public void DefaultSort_ParsesAgainstItsOwnWhitelist() =>
        SortOrder.Parse(null, _policy).IsSuccess.ShouldBeTrue();

    /// <summary>Both columns are non-null, so a keyset cursor can be built over either.</summary>
    [Fact]
    public void EverySortableField_SupportsKeysetPaging() =>
        _policy.SortableFields.ShouldAllBe(field => field.SupportsKeyset);

    [Fact]
    public void TheDefaultPageSize_FitsUnderTheCeiling() =>
        _policy.DefaultPageSize.ShouldBeLessThanOrEqualTo(_policy.MaxPageSize);
}
