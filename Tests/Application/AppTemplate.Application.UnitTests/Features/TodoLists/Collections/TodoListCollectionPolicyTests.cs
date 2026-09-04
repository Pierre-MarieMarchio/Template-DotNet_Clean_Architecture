using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Collections;

public sealed class TodoListCollectionPolicyTests
{
    private static readonly TodoListCollectionPolicy _policy = TodoListCollectionPolicy.Instance;

    /// <summary>
    /// Its own default is whitelisted by the same code a caller's <c>sort</c> string takes, so a
    /// typo in it fails here rather than shipping.
    /// </summary>
    [Fact]
    public void DefaultSort_ParsesAgainstItsOwnWhitelist() =>
        SortOrder.Parse(null, _policy).IsSuccess.ShouldBeTrue();

    [Fact]
    public void SortableFields_IsNotEmpty() => _policy.SortableFields.ShouldNotBeEmpty();

    [Fact]
    public void SortableFields_HasNoDuplicateNames() =>
        _policy.SortableFields.Select(field => field.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count()
            .ShouldBe(_policy.SortableFields.Count);

    [Fact]
    public void NameAndCreatedAt_SupportKeyset()
    {
        _policy.SortableFields.Single(field => field.Name == TodoListCollectionPolicy.NameField)
            .SupportsKeyset.ShouldBeTrue();
        _policy.SortableFields.Single(field => field.Name == TodoListCollectionPolicy.CreatedAtField)
            .SupportsKeyset.ShouldBeTrue();
    }

    /// <summary>Nullable columns cannot resume a keyset page: the row that minted the cursor could be skipped.</summary>
    [Fact]
    public void LastModifiedAt_IsOffsetOnly() =>
        _policy.SortableFields.Single(field => field.Name == TodoListCollectionPolicy.LastModifiedAtField)
            .SupportsKeyset.ShouldBeFalse();

    [Fact]
    public void Instance_IsASingleton() => TodoListCollectionPolicy.Instance.ShouldBeSameAs(_policy);

    [Fact]
    public void MaxPageSize_IsOneHundred() => _policy.MaxPageSize.ShouldBe(100);
}
