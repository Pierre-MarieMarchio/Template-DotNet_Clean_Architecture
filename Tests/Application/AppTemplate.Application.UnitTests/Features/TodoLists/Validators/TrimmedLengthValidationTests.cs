using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Application.Features.TodoLists.Validators;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Validators;

/// <summary>
/// Every validator here measures length after trimming, the same way the domain does. Without
/// it, a name of exactly the maximum length followed by a space would be refused by the
/// validator (400) and then accepted by the domain — two components disagreeing about the same
/// input. One test class because it is the same contract, repeated identically across every
/// string-length rule these validators declare.
/// </summary>
public sealed class TrimmedLengthValidationTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateTodoListCommandValidator_Accepts_AMaximumLengthNamePaddedWithWhitespace()
    {
        var command = new CreateTodoListCommand($"{new string('a', TodoListName.MaxLength)}  ");

        var result = await new CreateTodoListCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task RenameTodoListCommandValidator_Accepts_AMaximumLengthNamePaddedWithWhitespace()
    {
        var command = new RenameTodoListCommand(Guid.CreateVersion7(), $"  {new string('a', TodoListName.MaxLength)}");

        var result = await new RenameTodoListCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task AddTodoItemCommandValidator_Accepts_AMaximumLengthTitlePaddedWithWhitespace()
    {
        var command = new AddTodoItemCommand(
            Guid.CreateVersion7(),
            $"  {new string('a', TodoItemTitle.MaxLength)}  ",
            null,
            null);

        var result = await new AddTodoItemCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task AddTodoItemCommandValidator_Accepts_AMaximumLengthDescriptionPaddedWithWhitespace()
    {
        var command = new AddTodoItemCommand(
            Guid.CreateVersion7(),
            "Buy milk",
            $"  {new string('a', TodoItem.MaxDescriptionLength)}",
            null);

        var result = await new AddTodoItemCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task AddTodoItemCommandValidator_Accepts_AMaximumLengthTagPaddedWithWhitespace()
    {
        var command = new AddTodoItemCommand(
            Guid.CreateVersion7(),
            "Buy milk",
            null,
            [$"{new string('a', Tag.MaxLength)}  "]);

        var result = await new AddTodoItemCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeTrue();
    }

    /// <summary>
    /// The behavior the spec calls out explicitly: a malformed tag on <c>AddTodoItem</c> is now a
    /// 400 from this validator, not a domain exception the use case turns into a 409.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddTodoItemCommandValidator_Rejects_ABlankTag(string tag)
    {
        var command = new AddTodoItemCommand(Guid.CreateVersion7(), "Buy milk", null, ["urgent", tag]);

        var result = await new AddTodoItemCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task AddTodoItemCommandValidator_Rejects_ATagOneCharacterBeyondTheMaximum()
    {
        var command = new AddTodoItemCommand(
            Guid.CreateVersion7(),
            "Buy milk",
            null,
            [new string('a', Tag.MaxLength + 1)]);

        var result = await new AddTodoItemCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateTodoItemCommandValidator_Accepts_AMaximumLengthTitlePaddedWithWhitespace()
    {
        var command = new UpdateTodoItemCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            $"  {new string('a', TodoItemTitle.MaxLength)}",
            null);

        var result = await new UpdateTodoItemCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task AddTagToTodoItemCommandValidator_Accepts_AMaximumLengthTagPaddedWithWhitespace()
    {
        var command = new AddTagToTodoItemCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            $"{new string('a', Tag.MaxLength)}  ");

        var result = await new AddTagToTodoItemCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ReplaceTodoItemTagsCommandValidator_Accepts_AMaximumLengthTagPaddedWithWhitespace()
    {
        var command = new ReplaceTodoItemTagsCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            [$"{new string('a', Tag.MaxLength)}  "]);

        var result = await new ReplaceTodoItemTagsCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ReplaceTodoItemTagsCommandValidator_Rejects_MoreThanTheTagCap()
    {
        var command = new ReplaceTodoItemTagsCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            [.. Enumerable.Range(0, TodoItem.MaxTags + 1).Select(i => $"tag-{i}")]);

        var result = await new ReplaceTodoItemTagsCommandValidator().ValidateAsync(command, TestToken);

        result.IsValid.ShouldBeFalse();
    }
}
