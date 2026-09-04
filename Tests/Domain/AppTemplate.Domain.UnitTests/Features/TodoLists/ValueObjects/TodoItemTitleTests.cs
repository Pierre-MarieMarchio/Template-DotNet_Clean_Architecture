using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.ValueObjects;

public sealed class TodoItemTitleTests
{
    [Fact]
    public void Create_KeepsAnAlreadyNormalisedValue() =>
        TodoItemTitle.Create("Buy milk").Value.ShouldBe("Buy milk");

    [Fact]
    public void Create_TrimsSurroundingWhitespace() =>
        TodoItemTitle.Create("  Buy milk \t ").Value.ShouldBe("Buy milk");

    /// <summary>
    /// Unlike a tag, a title is something a person reads, so its casing and its inner spacing
    /// are theirs to choose. Adding a <c>ToLowerInvariant</c> here turns this red.
    /// </summary>
    [Fact]
    public void Create_PreservesCasingAndInnerWhitespace() =>
        TodoItemTitle.Create("Buy  Milk AND bread").Value.ShouldBe("Buy  Milk AND bread");

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("     ")]
    [InlineData("\t\n")]
    public void Create_Rejects_ABlankValue(string value)
    {
        var exception = Should.Throw<DomainException>(() => TodoItemTitle.Create(value));

        exception.Message.ShouldContain("title");
    }

    [Fact]
    public void Create_Rejects_ANullValue() =>
        Should.Throw<DomainException>(() => TodoItemTitle.Create(null!));

    [Fact]
    public void Create_Accepts_ExactlyTheMaximumLength() =>
        TodoItemTitle.Create(new string('a', TodoItemTitle.MaxLength)).Value.Length
            .ShouldBe(TodoItemTitle.MaxLength);

    [Fact]
    public void Create_Rejects_OneCharacterBeyondTheMaximumLength()
    {
        var exception = Should.Throw<DomainException>(
            () => TodoItemTitle.Create(new string('a', TodoItemTitle.MaxLength + 1)));

        exception.Message.ShouldContain("exceed");
    }

    [Fact]
    public void Create_MeasuresTheLengthAfterTrimming() =>
        TodoItemTitle.Create($"   {new string('a', TodoItemTitle.MaxLength)}   ").Value.Length
            .ShouldBe(TodoItemTitle.MaxLength);

    [Fact]
    public void Equality_IsByValue()
    {
        TodoItemTitle.Create("Buy milk").ShouldBe(TodoItemTitle.Create("  Buy milk  "));
        TodoItemTitle.Create("Buy milk").GetHashCode()
            .ShouldBe(TodoItemTitle.Create("Buy milk").GetHashCode());
    }

    /// <summary>
    /// Casing is part of the value, so it is part of the identity too. The list's uniqueness rule
    /// is case-insensitive on purpose and does its own comparison; that is a rule about a set of
    /// titles, not about what makes two titles the same value.
    /// </summary>
    [Fact]
    public void Equality_IsCaseSensitive() =>
        TodoItemTitle.Create("Buy milk").ShouldNotBe(TodoItemTitle.Create("buy milk"));

    [Fact]
    public void ToString_ReturnsTheNormalisedValue() =>
        TodoItemTitle.Create("  Buy milk  ").ToString().ShouldBe("Buy milk");

    [Fact]
    public void Value_HasNoSetter() =>
        typeof(TodoItemTitle).GetProperty(nameof(TodoItemTitle.Value))!.SetMethod.ShouldBeNull();

    [Fact]
    public void TheOnlyWayToBuildATitle_IsTheFactory() =>
        typeof(TodoItemTitle).GetConstructors().ShouldBeEmpty();

    /// <summary>
    /// A tripwire on the value of the limit, not on the mechanism: every other length test above
    /// reads the constant and is therefore blind to a change in the constant itself. The column is
    /// sized from this number, so widening it is a schema decision and has to be a deliberate one.
    /// </summary>
    [Fact]
    public void TheMaximumLength_IsTwoHundredCharacters() => TodoItemTitle.MaxLength.ShouldBe(200);
}
