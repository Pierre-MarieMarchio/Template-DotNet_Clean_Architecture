using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Files;

/// <summary>
/// Whether one caller can reach another caller's file, over HTTP, on every entry point that takes an
/// id.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Files feature had no test at this level at all.</b> Ownership is enforced in one place —
/// <c>StoredFileAccess.LoadOwnedAsync</c> — which is the right shape and is exactly why a single
/// endpoint that stopped going through it would be invisible: every other endpoint would keep
/// passing. So this asserts the entry points rather than the gate, and it enumerates all four of
/// them deliberately. One forgotten route is the whole vulnerability.
/// </para>
/// <para>
/// A registration is enough to test with, and no bytes are deposited. What is under test is who may
/// name a file, which is settled before anything reads content; a pending file also lets the
/// download endpoint be asked the question in its most interesting form, since the owner's own
/// answer there is a refusal too — for a different reason, with a different status.
/// </para>
/// </remarks>
public sealed class StoredFileOwnershipTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _filesRoute = "/api/v1/files";

    [Fact]
    public async Task AnotherUsersFile_IsNotFoundOnEveryEntryPointThatNamesOne()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (intruder, _, _) = await SignInAsync("intruder");

        var registered = await RegisterFileAsync(owner);

        // The control, and it is not decoration: without it every assertion below is satisfied by an
        // id that names nothing, and the test would pass against a feature that answers 404 to
        // everyone.
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
            "DELETE, which would destroy somebody else's row and let its bytes be reclaimed",
            () => intruder.DeleteAsync(new Uri($"{_filesRoute}/{registered.Id}", UriKind.Relative), TestToken));

        // Still there afterwards. A 404 that had nonetheless carried out the write would be worse
        // than a 403, and nothing above would have noticed.
        using var afterwards = await owner.GetAsync(
            new Uri($"{_filesRoute}/{registered.Id}", UriKind.Relative),
            TestToken);

        afterwards.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "every refusal above must also have refused to act. A file deleted or confirmed by " +
            "somebody who was answered 404 is a 404 that lied.");
    }

    /// <summary>
    /// The download endpoint's two refusals must not be confusable: its owner is told the file is not
    /// ready, and a stranger is told it does not exist.
    /// </summary>
    /// <remarks>
    /// This is the one endpoint where the owner is refused too, so it is the one where a lazy
    /// implementation could answer both callers the same way and look correct from either side alone.
    /// The statuses have to differ, because the information they carry differs: "wait" is true only
    /// for the person entitled to wait.
    /// </remarks>
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

    /// <summary>
    /// Under <c>If-Match: *</c>, confirming another user's file and confirming one that never existed
    /// have to be indistinguishable — body included.
    /// </summary>
    /// <remarks>
    /// The wildcard changes the status: a file the caller cannot see fails the precondition rather
    /// than answering 404, which is deliberate and documented on the endpoint. That makes it worth
    /// asserting, because the interesting property survives the change — both answers are the same
    /// answer, so the pair cannot be used to ask whether an id exists.
    /// </remarks>
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

    /// <summary>
    /// The registration response carries a signed write URL, which is a credential.
    /// </summary>
    /// <remarks>
    /// A POST gets no <c>Cache-Control</c> from this API's default, so without the endpoint's own
    /// <c>[NoStore]</c> the response would say nothing at all about a bearer right to write into the
    /// bucket. Asserted over HTTP rather than on the attribute: what protects the credential is the
    /// header that arrives.
    /// </remarks>
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
            "quarterly-report.png",
            "image/png",
            SizeInBytes: 4_096,

            // Any 64 hexadecimal characters: nothing is deposited here, so no digest is ever compared
            // against content. What matters is that the value is one the domain accepts.
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

    /// <summary>
    /// Asserts a refusal that says nothing, and names the entry point in the failure so a red test
    /// points at the route rather than at this helper.
    /// </summary>
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
