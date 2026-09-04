using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.TodoLists.Contracts;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

/// <summary>
/// A refused invariant is a proper HTTP status with a stable code — never a 500 carrying an
/// exception message.
/// </summary>
public sealed class AggregateInvariantTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ADuplicateItemTitle_Is409WithTheInvariantCode()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        await AddTodoItemAsync(client, listId, "Buy milk");

        using var response = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items",
            new AddTodoItemRequest("Buy milk", null, null),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldBe("todoList.invariantViolated");
        problem.Status.ShouldBe(409);
        problem.Title.ShouldBe("Conflict");
        problem.Detail.ShouldBe("This list already contains an item titled 'Buy milk'.");
    }

    /// <summary>
    /// The rule is case-insensitive, which is why it lives in the aggregate rather than in a unique
    /// index: a B-tree unique index would enforce a different and weaker rule.
    /// </summary>
    [Theory]
    [InlineData("buy milk")]
    [InlineData("BUY MILK")]
    [InlineData("  Buy Milk  ")]
    public async Task ATitleDifferingOnlyInCaseOrWhitespace_IsAlsoADuplicate(string variant)
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        await AddTodoItemAsync(client, listId, "Buy milk");

        using var response = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items",
            new AddTodoItemRequest(variant, null, null),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("todoList.invariantViolated");
    }

    [Fact]
    public async Task CompletingAnAlreadyCompletedItem_Is409WithTheInvariantCode()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        using var first = await CompleteAsync(client, listId, itemId);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var second = await CompleteAsync(client, listId, itemId);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await ApiJson.ReadProblemAsync(second, TestToken);
        problem.Code.ShouldBe("todoList.invariantViolated");
        problem.Detail.ShouldBe("Item 'Do the thing' is already completed.");
    }

    /// <summary>
    /// The negative statement that makes the rest of this class meaningful: none of these refusals is
    /// a 500, and none of them leaks a stack trace or an exception message the product did not write.
    /// </summary>
    [Fact]
    public async Task NoRefusedInvariant_ProducesAServerError()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        using (var completed = await CompleteAsync(client, listId, itemId))
        {
            completed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var duplicateTitle = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items",
            new AddTodoItemRequest("Do the thing", null, null),
            TestToken))
        {
            await AssertCleanRefusalAsync(duplicateTitle);
        }

        using (var alreadyCompleted = await CompleteAsync(client, listId, itemId))
        {
            await AssertCleanRefusalAsync(alreadyCompleted);
        }

        using (var overlongName = await client.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListCommand(new string('x', TodoListName.MaxLength + 1)),
            TestToken))
        {
            await AssertCleanRefusalAsync(overlongName);
        }

        using (var overlongTitle = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items",
            new AddTodoItemRequest(new string('x', TodoItemTitle.MaxLength + 1), null, null),
            TestToken))
        {
            await AssertCleanRefusalAsync(overlongTitle);
        }
    }

    [Fact]
    public async Task AnOverlongNameOrTitle_IsAValidationFailureRatherThanADomainException()
    {
        var (client, _, _) = await SignInAsync();

        // The shape validator and the value object share the same bound, so the request is refused
        // before the aggregate is ever asked — a DomainException here would mean the two disagree.
        using var response = await client.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListCommand(new string('x', TodoListName.MaxLength + 1)),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("todoList.validationFailed");

        using var atTheBound = await client.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListCommand(new string('x', TodoListName.MaxLength)),
            TestToken);

        atTheBound.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AMalformedBody_Is400AndNotA500()
    {
        var (client, _, _) = await SignInAsync();

        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(new Uri(TodoListsRoute, UriKind.Relative), content, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A refusal the product decided on: a 4xx, a stable code, and no sign of the last-resort handler
    /// — which is what a <c>traceId</c> or an "Unexpected error" title would mean.
    /// </summary>
    private static async Task AssertCleanRefusalAsync(HttpResponseMessage response)
    {
        ((int)response.StatusCode).ShouldBeInRange(400, 499);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);

        problem.Code.ShouldNotBeNullOrWhiteSpace();
        problem.Title.ShouldNotBe("Unexpected error");
        problem.Body.ShouldNotContain("traceId");
        problem.Body.ShouldNotContain("Exception");
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid listId, Guid itemId) =>
        client.PostAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}/complete", UriKind.Relative),
            content: null,
            TestToken);
}
