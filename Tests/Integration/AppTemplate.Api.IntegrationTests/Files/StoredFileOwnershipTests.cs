using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.Features.Files.Controllers;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Files;

public sealed class StoredFileOwnershipTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _filesRoute = "/api/v1/files";

    private const int _entryPointsNamingAFile = 5;

    [Fact]
    public async Task AnotherUsersFile_IsNotFoundOnEveryEntryPointThatNamesOne()
    {
        new[] { typeof(FilesController), typeof(FileTagsController) }
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Count(action => action.GetParameters().Any(parameter => parameter.Name == "fileId"))
            .ShouldBe(
                _entryPointsNamingAFile,
                "an action that names a file is an action a stranger can aim at an id that is not " +
                "theirs, so each one is asked the question below. This count is what says they all " +
                "are.");

        var (owner, _, _) = await SignInAsync("owner");
        var (intruder, _, _) = await SignInAsync("intruder");

        var registered = await RegisterFileAsync(owner);

        using (var toItsOwner = await owner.GetAsync(new Uri($"{_filesRoute}/{registered.Id}", UriKind.Relative), TestToken))
        {
            toItsOwner.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                "the file has to be reachable by the person who registered it, or the refusals below " +
                "prove nothing about ownership.");
        }

        await ShouldBeNotFoundAsync(
            "GET the representation",
            () => intruder.GetAsync(new Uri($"{_filesRoute}/{registered.Id}", UriKind.Relative), TestToken));

        await ShouldBeNotFoundAsync(
            "GET the content, which is where a leak hands out a signed URL rather than a document",
            () => intruder.GetAsync(new Uri($"{_filesRoute}/{registered.Id}/content", UriKind.Relative), TestToken));

        await ShouldBeNotFoundAsync(
            "POST the confirmation, which would move somebody else's file into a servable state",
            () => intruder.PostAsync(new Uri($"{_filesRoute}/{registered.Id}/confirm", UriKind.Relative), null, TestToken));

        await ShouldBeNotFoundAsync(
            "PUT the tags, which would relabel somebody else's file",
            () => intruder.PutAsJsonAsync(
                new Uri($"{_filesRoute}/{registered.Id}/tags", UriKind.Relative),
                new ReplaceStoredFileTagsRequest(["intruded"]),
                TestToken));

        await ShouldBeNotFoundAsync(
            "DELETE, which would destroy somebody else's row and let its bytes be reclaimed",
            () => intruder.DeleteAsync(new Uri($"{_filesRoute}/{registered.Id}", UriKind.Relative), TestToken));

        using var afterwards = await owner.GetAsync(
            new Uri($"{_filesRoute}/{registered.Id}", UriKind.Relative),
            TestToken);

        afterwards.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "every refusal above must also have refused to act. A file deleted or confirmed by " +
            "somebody who was answered 404 is a 404 that lied.");
    }

    [Fact]
    public async Task TheDownloadEndpoint_TellsItsOwnerToWaitAndTellsAStrangerNothing()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (intruder, _, _) = await SignInAsync("intruder");

        var registered = await RegisterFileAsync(owner);
        var content = new Uri($"{_filesRoute}/{registered.Id}/content", UriKind.Relative);

        using var toItsOwner = await owner.GetAsync(content, TestToken);
        using var toAStranger = await intruder.GetAsync(content, TestToken);

        toItsOwner.StatusCode.ShouldBe(
            HttpStatusCode.Conflict,
            "the deposit was never confirmed, so its owner is refused a grant for a key that may " +
            "hold nothing — and told so.");

        toAStranger.StatusCode.ShouldBe(
            HttpStatusCode.NotFound,
            "a stranger learns nothing, not even that the id is one this installation has heard of.");
    }

    [Fact]
    public async Task UnderAWildcardPrecondition_AForeignFileAndAMissingFileAnswerIdentically()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (intruder, _, _) = await SignInAsync("intruder");

        var registered = await RegisterFileAsync(owner);

        using var toAForeignFile = await ConfirmWithWildcardAsync(intruder, registered.Id);
        using var toAMissingFile = await ConfirmWithWildcardAsync(intruder, Guid.CreateVersion7());

        toAForeignFile.StatusCode.ShouldBe(toAMissingFile.StatusCode);

        var foreign = await ApiJson.ReadProblemAsync(toAForeignFile, TestToken);
        var missing = await ApiJson.ReadProblemAsync(toAMissingFile, TestToken);

        foreign.BodyWithoutTraceId.ShouldBe(
            missing.BodyWithoutTraceId,
            "these two bodies are what an enumeration attack reads. Any difference between them — a " +
            "code, a detail, a title — turns this endpoint into a way of asking which ids exist.");
    }

    [Fact]
    public async Task AnotherUsersFile_IsAbsentFromTheListing()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (intruder, _, _) = await SignInAsync("intruder");

        var registered = await RegisterFileAsync(owner);

        using var response = await intruder.GetAsync(new Uri(_filesRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var page = await ApiJson.ReadAsync<PagedResponse<StoredFileResponse>>(response, TestToken);

        page.Items.ShouldNotContain(
            item => item.Id == registered.Id,
            "the listing filters on the owner inside the query, so another caller's row must never " +
            "reach the projection at all.");
    }

    [Fact]
    public async Task TheRegistrationResponse_ForbidsStoringTheGrantItCarries()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(
            new Uri(_filesRoute, UriKind.Relative),
            ARegistration(),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        response.Headers.CacheControl?.NoStore.ShouldBe(
            true,
            "the body holds a signed upload URL. Anything that may store this response stores a " +
            "credential, and the URL outlives the response by the whole grant lifetime.");
    }

    private static RegisterFileRequest ARegistration() =>
        new(
            "document",
            "quarterly-report.pdf",
            "application/pdf",
            SizeInBytes: 4_096,

            new string('a', 64));

    private static async Task<StoredFileRegistrationResponse> RegisterFileAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri(_filesRoute, UriKind.Relative),
            ARegistration(),
            TestToken);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Registering a file failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return await ApiJson.ReadAsync<StoredFileRegistrationResponse>(response, TestToken);
    }

    private static async Task<HttpResponseMessage> ConfirmWithWildcardAsync(HttpClient client, Guid fileId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{_filesRoute}/{fileId}/confirm", UriKind.Relative));

        request.Headers.IfMatch.Add(EntityTagHeaderValue.Any);

        return await client.SendAsync(request, TestToken);
    }

    private static async Task ShouldBeNotFoundAsync(string entryPoint, Func<Task<HttpResponseMessage>> call)
    {
        using var response = await call();

        response.StatusCode.ShouldBe(
            HttpStatusCode.NotFound,
            $"{entryPoint}: another caller's file has to be indistinguishable from one that does not " +
            $"exist. This entry point answered {(int)response.StatusCode}, and a 403 would confirm " +
            "the file is real.");
    }
}
