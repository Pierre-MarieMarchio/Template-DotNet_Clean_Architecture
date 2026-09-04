using System.Reflection;
using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.TodoLists.Entities;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Common.Abstractions;

/// <summary>
/// Tests for <see cref="IAuditable"/>, exercised through the one aggregate that
/// implements it. The point of the interface is not that the values can be set — it is
/// that only a persistence interceptor holding the interface can set them.
/// </summary>
public sealed class AuditableTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewAggregate_HasNoAuditStamps()
    {
        var list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);

        list.CreatedAt.ShouldBe(default);
        list.CreatedBy.ShouldBeNull();
        list.LastModifiedAt.ShouldBeNull();
        list.LastModifiedBy.ShouldBeNull();
    }

    [Fact]
    public void SetCreated_StampsTheCreationValues()
    {
        var list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);
        var actor = Guid.CreateVersion7();

        ((IAuditable)list).SetCreated(_now, actor);

        list.CreatedAt.ShouldBe(_now);
        list.CreatedBy.ShouldBe(actor);
        list.LastModifiedAt.ShouldBeNull();
        list.LastModifiedBy.ShouldBeNull();
    }

    [Fact]
    public void SetLastModified_StampsTheModificationValues()
    {
        var list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);
        var actor = Guid.CreateVersion7();

        ((IAuditable)list).SetLastModified(_now, actor);

        list.LastModifiedAt.ShouldBe(_now);
        list.LastModifiedBy.ShouldBe(actor);
        list.CreatedAt.ShouldBe(default);
    }

    /// <summary>A change made by a background job has no acting user.</summary>
    [Fact]
    public void TheActingUser_MayBeAbsent()
    {
        var list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);

        ((IAuditable)list).SetCreated(_now, null);
        ((IAuditable)list).SetLastModified(_now, null);

        list.CreatedBy.ShouldBeNull();
        list.LastModifiedBy.ShouldBeNull();
    }

    /// <summary>
    /// The stamping methods are implemented explicitly, so application code holding a
    /// <see cref="TodoList"/> cannot reach them without deliberately casting to the
    /// interface. Turning either into a public method turns this red.
    /// </summary>
    [Theory]
    [InlineData("SetCreated")]
    [InlineData("SetLastModified")]
    public void TheStampingMethods_AreNotPartOfTheAggregatesPublicSurface(string methodName) =>
        typeof(TodoList)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeNull();

    /// <summary>
    /// The audit properties are read-only from outside the assembly, which is what stops
    /// a caller from forging a creation date or an author.
    /// </summary>
    [Theory]
    [InlineData(nameof(TodoList.CreatedAt))]
    [InlineData(nameof(TodoList.CreatedBy))]
    [InlineData(nameof(TodoList.LastModifiedAt))]
    [InlineData(nameof(TodoList.LastModifiedBy))]
    public void TheAuditProperties_HaveNoPubliclyReachableSetter(string propertyName)
    {
        var setter = typeof(TodoList).GetProperty(propertyName)?.SetMethod;

        (setter is null || !setter.IsPublic).ShouldBeTrue(
            $"'{propertyName}' must not expose a public setter.");
    }
}
