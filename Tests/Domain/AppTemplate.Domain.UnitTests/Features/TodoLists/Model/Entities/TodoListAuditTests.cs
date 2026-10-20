using System.Reflection;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Model.Entities;

public sealed class TodoListAuditTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewAggregate_HasNoAuditStamps()
    {
        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Groceries", _now);

        list.CreatedAt.ShouldBe(default);
        list.CreatedBy.ShouldBeNull();
        list.LastModifiedAt.ShouldBeNull();
        list.LastModifiedBy.ShouldBeNull();
    }

    [Fact]
    public void SetCreated_StampsTheCreationValues()
    {
        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Groceries", _now);
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
        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Groceries", _now);
        var actor = Guid.CreateVersion7();

        ((IAuditable)list).SetLastModified(_now, actor);

        list.LastModifiedAt.ShouldBe(_now);
        list.LastModifiedBy.ShouldBe(actor);
        list.CreatedAt.ShouldBe(default);
    }

    [Fact]
    public void TheActingUser_MayBeAbsent()
    {
        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Groceries", _now);

        ((IAuditable)list).SetCreated(_now, null);
        ((IAuditable)list).SetLastModified(_now, null);

        list.CreatedBy.ShouldBeNull();
        list.LastModifiedBy.ShouldBeNull();
    }

    [Theory]
    [InlineData("SetCreated")]
    [InlineData("SetLastModified")]
    public void TheStampingMethods_AreNotPartOfTheAggregatesPublicSurface(string methodName) =>
        typeof(TodoList)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeNull();

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
