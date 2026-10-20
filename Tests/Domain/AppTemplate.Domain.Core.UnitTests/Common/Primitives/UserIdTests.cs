using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.Core.UnitTests.Common.Primitives;

public sealed class UserIdTests
{
    [Fact]
    public void Create_KeepsTheIdentifier()
    {
        var value = Guid.CreateVersion7();

        UserId.Create(value).Value.ShouldBe(value);
    }

    [Fact]
    public void Create_Rejects_AnEmptyIdentifier()
    {
        var exception = Should.Throw<DomainException>(() => UserId.Create(Guid.Empty));

        exception.Message.ShouldContain("owner");
    }

    [Fact]
    public void CreateOptional_AnswersNull_ForNoIdentifierAtAll() =>
        UserId.CreateOptional(null).ShouldBeNull();

    [Fact]
    public void CreateOptional_AnswersNull_ForAnEmptyIdentifier() =>
        UserId.CreateOptional(Guid.Empty).ShouldBeNull();

    [Fact]
    public void CreateOptional_KeepsARealIdentifier()
    {
        var value = Guid.CreateVersion7();

        UserId.CreateOptional(value)!.Value.ShouldBe(value);
    }

    [Fact]
    public void Equality_IsDecidedByTheIdentifier()
    {
        var value = Guid.CreateVersion7();

        var left = UserId.Create(value);
        var right = UserId.Create(value);

        left.ShouldBe(right);
        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equality_SeparatesTwoOwners()
    {
        var left = UserId.Create(Guid.CreateVersion7());
        var right = UserId.Create(Guid.CreateVersion7());

        (left == right).ShouldBeFalse();
        (left != right).ShouldBeTrue();
    }

    [Fact]
    public void ToString_IsTheIdentifierItself()
    {
        var value = Guid.CreateVersion7();

        UserId.Create(value).ToString().ShouldBe(value.ToString());
    }
}
