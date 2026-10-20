using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Domain.Features.TodoLists.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

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
        problem.Code.ShouldBe("domain.invariantViolated");
        problem.Status.ShouldBe(409);
        problem.Title.ShouldBe("Conflict");
        problem.Detail.ShouldBe("This list already contains an item titled 'Buy milk'.");
    }

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
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("domain.invariantViolated");
    }

    [Fact]
    public async Task CompletingAnAlreadyCompletedItem_Is409WithTheInvariantCode()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        using var first = await CompleteAsync(client, listId, itemId);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var second = await CompleteAsync(client, listId, itemId);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await ApiJson.ReadProblemAsync(second, TestToken);
        problem.Code.ShouldBe("domain.invariantViolated");
        problem.Detail.ShouldBe("Item 'Do the thing' is already completed.");
    }

    [Fact]
    public async Task NoRefusedInvariant_ProducesAServerError()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        using (var completed = await CompleteAsync(client, listId, itemId))
        {
            completed.StatusCode.ShouldBe(HttpStatusCode.OK);
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
            new CreateTodoListRequest(new string('x', TodoListName.MaxLength + 1)),
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

        using var response = await client.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListRequest(new string('x', TodoListName.MaxLength + 1)),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("request.validationFailed");

        using var atTheBound = await client.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListRequest(new string('x', TodoListName.MaxLength)),
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

    private static async Task AssertCleanRefusalAsync(HttpResponseMessage response)
    {
        ((int)response.StatusCode).ShouldBeInRange(400, 499);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);

        problem.Code.ShouldNotBeNullOrWhiteSpace();
        problem.Title.ShouldNotBe("Unexpected error");
        problem.Body.ShouldNotContain("Exception");
        problem.Body.ShouldNotContain("StackTrace");
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid listId, Guid itemId) =>
        client.PostAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}/complete", UriKind.Relative),
            content: null,
            TestToken);
}
