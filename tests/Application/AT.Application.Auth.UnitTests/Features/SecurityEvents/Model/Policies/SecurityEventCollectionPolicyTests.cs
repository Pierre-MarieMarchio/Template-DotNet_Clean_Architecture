using AppTemplate.Application.Auth.Features.SecurityEvents.Model.Policies;
using AppTemplate.Application.Core.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.SecurityEvents.Model.Policies;

public sealed class SecurityEventCollectionPolicyTests
{
    private static readonly SecurityEventCollectionPolicy _policy = SecurityEventCollectionPolicy.Instance;

    [Fact]
    public void DefaultSort_ParsesAgainstItsOwnWhitelist() =>
        SortOrder.Parse(null, _policy).IsSuccess.ShouldBeTrue();

    [Fact]
    public void NoSortableField_PromisesKeysetPaging() =>
        _policy.SortableFields.ShouldAllBe(field => !field.SupportsKeyset);

    [Fact]
    public void TheDefaultPageSize_FitsUnderTheCeiling() =>
        _policy.DefaultPageSize.ShouldBeLessThanOrEqualTo(_policy.MaxPageSize);
}
