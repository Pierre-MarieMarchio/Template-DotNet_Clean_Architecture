using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Conventions;

/// <summary>
/// Every rule whose <c>Must</c> dereferences the value carries <c>Cascade(CascadeMode.Stop)</c>.
/// FluentValidation runs the remaining rules for a property even after <c>NotEmpty</c> has failed,
/// so without the cascade a null value is not a field error but a <c>NullReferenceException</c> —
/// which reaches the caller as a 500 naming nothing, and no request the pipeline accepts is
/// supposed to produce one.
/// <para>
/// A null gets this far from a body that is entirely well formed. The implicit <c>[Required]</c> a
/// non-nullable reference type carries does not extend to the elements of a collection, so
/// <c>{"title":"x","tags":["a",null]}</c> deserialises, passes model binding, and arrives here with
/// a null inside the list. Removing one of the cascades below turns its test from a validation
/// failure into a dereference.
/// </para>
/// One class because it is the same contract, repeated identically across every such rule.
/// </summary>
public sealed class NullDereferenceValidationTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateTodoListCommandValidator_Rejects_ANullName() =>
        (await new CreateTodoListCommandValidator().ValidateAsync(new CreateTodoListCommand(null!), TestToken))
        .IsValid.ShouldBeFalse();

    [Fact]
    public async Task RenameTodoListCommandValidator_Rejects_ANullName() =>
        (await new RenameTodoListCommandValidator()
                .ValidateAsync(new RenameTodoListCommand(Guid.CreateVersion7(), null!), TestToken))
        .IsValid.ShouldBeFalse();

    [Fact]
    public async Task AddTodoItemCommandValidator_Rejects_ANullTitle() =>
        (await new AddTodoItemCommandValidator()
                .ValidateAsync(new AddTodoItemCommand(Guid.CreateVersion7(), null!, null, null), TestToken))
        .IsValid.ShouldBeFalse();

    [Fact]
    public async Task AddTodoItemCommandValidator_Rejects_ANullTagAmongValidOnes() =>
        (await new AddTodoItemCommandValidator()
                .ValidateAsync(
                    new AddTodoItemCommand(Guid.CreateVersion7(), "Buy milk", null, ["urgent", null!]),
                    TestToken))
        .IsValid.ShouldBeFalse();

    [Fact]
    public async Task UpdateTodoItemCommandValidator_Rejects_ANullTitle() =>
        (await new UpdateTodoItemCommandValidator()
                .ValidateAsync(
                    new UpdateTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), null!, null),
                    TestToken))
        .IsValid.ShouldBeFalse();

    [Fact]
    public async Task AddTagToTodoItemCommandValidator_Rejects_ANullTag() =>
        (await new AddTagToTodoItemCommandValidator()
                .ValidateAsync(
                    new AddTagToTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), null!),
                    TestToken))
        .IsValid.ShouldBeFalse();

    [Fact]
    public async Task RemoveTagFromTodoItemCommandValidator_Rejects_ANullTag() =>
        (await new RemoveTagFromTodoItemCommandValidator()
                .ValidateAsync(
                    new RemoveTagFromTodoItemCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), null!),
                    TestToken))
        .IsValid.ShouldBeFalse();

    [Fact]
    public async Task ReplaceTodoItemTagsCommandValidator_Rejects_ANullTagAmongValidOnes() =>
        (await new ReplaceTodoItemTagsCommandValidator()
                .ValidateAsync(
                    new ReplaceTodoItemTagsCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), ["urgent", null!]),
                    TestToken))
        .IsValid.ShouldBeFalse();
}
