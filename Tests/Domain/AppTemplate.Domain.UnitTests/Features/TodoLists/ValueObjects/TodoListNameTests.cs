using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.ValueObjects;

public sealed class TodoListNameTests
{
    [Fact]
    public void Create_KeepsAnAlreadyNormalisedValue() =>
        TodoListName.Create("Groceries").Value.ShouldBe("Groceries");

    [Fact]
    public void Create_TrimsSurroundingWhitespace() =>
        TodoListName.Create("  Groceries \t ").Value.ShouldBe("Groceries");

    /// <summary>
    /// Unlike a tag, a list name is something a person reads, so its casing is theirs to
    /// choose. Adding a <c>ToLowerInvariant</c> here turns this red.
    /// </summary>
    [Fact]
    public void Create_PreservesCasing() =>
        TodoListName.Create("Weekly SHOPPING list").Value.ShouldBe("Weekly SHOPPING list");

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("     ")]
    [InlineData("\t\n")]
    public void Create_Rejects_ABlankValue(string value)
    {
        var exception = Should.Throw<DomainException>(() => TodoListName.Create(value));

        exception.Message.ShouldContain("name");
    }

    [Fact]
    public void Create_Rejects_ANullValue() =>
        Should.Throw<DomainException>(() => TodoListName.Create(null!));

    [Fact]
    public void Create_Accepts_ExactlyTheMaximumLength() =>
        TodoListName.Create(new string('a', TodoListName.MaxLength)).Value.Length
            .ShouldBe(TodoListName.MaxLength);

    [Fact]
    public void Create_Rejects_OneCharacterBeyondTheMaximumLength()
    {
        var exception = Should.Throw<DomainException>(
            () => TodoListName.Create(new string('a', TodoListName.MaxLength + 1)));

        exception.Message.ShouldContain("exceed");
    }

    [Fact]
    public void Create_MeasuresTheLengthAfterTrimming() =>
        TodoListName.Create($"   {new string('a', TodoListName.MaxLength)}   ").Value.Length
            .ShouldBe(TodoListName.MaxLength);

    [Fact]
    public void Equality_IsByValue()
    {
        TodoListName.Create("Groceries").ShouldBe(TodoListName.Create("  Groceries  "));
        TodoListName.Create("Groceries").GetHashCode()
            .ShouldBe(TodoListName.Create("Groceries").GetHashCode());
    }

    /// <summary>Casing is part of the value, so it is part of the identity too.</summary>
    [Fact]
    public void Equality_IsCaseSensitive() =>
        TodoListName.Create("Groceries").ShouldNotBe(TodoListName.Create("groceries"));

    [Fact]
    public void ToString_ReturnsTheNormalisedValue() =>
        TodoListName.Create("  Groceries  ").ToString().ShouldBe("Groceries");

    [Fact]
    public void Value_HasNoSetter() =>
        typeof(TodoListName).GetProperty(nameof(TodoListName.Value))!.SetMethod.ShouldBeNull();

    [Fact]
    public void TheOnlyWayToBuildAName_IsTheFactory() =>
        typeof(TodoListName).GetConstructors().ShouldBeEmpty();
}
