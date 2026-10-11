using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Core.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Auth.Policies;

public sealed class SecurityEventCollectionPolicyTests
{
    private static readonly SecurityEventCollectionPolicy _policy = SecurityEventCollectionPolicy.Instance;

    /// <summary>Its own default is whitelisted by the code a caller's sort takes, so a typo fails here.</summary>
    [Fact]
    public void DefaultSort_ParsesAgainstItsOwnWhitelist() =>
        SortOrder.Parse(null, _policy).IsSuccess.ShouldBeTrue();

    /// <summary>
    /// A trail is read from the top and never keyset-paginated, so no field here may promise a
    /// cursor: the one sortable field is offset-only by design, not by omission.
    /// </summary>
    [Fact]
    public void NoSortableField_PromisesKeysetPaging() =>
        _policy.SortableFields.ShouldAllBe(field => !field.SupportsKeyset);

    [Fact]
    public void TheDefaultPageSize_FitsUnderTheCeiling() =>
        _policy.DefaultPageSize.ShouldBeLessThanOrEqualTo(_policy.MaxPageSize);
}
