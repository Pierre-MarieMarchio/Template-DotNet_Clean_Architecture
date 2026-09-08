using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Domain.Features.Files.Entities;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Files;

/// <summary>
/// Labelling a file, over HTTP and through the database.
/// </summary>
/// <remarks>
/// The rules under test are not this feature's: normalisation, de-duplication and the cap live in
/// <c>TagSet</c>, which a to-do item obeys through the same code, and they have their own unit
/// tests. What is asserted here is that a file is genuinely subject to them — that the set survives
/// the round trip through the tag table and comes back through the read projection, which no unit
/// test can say.
/// </remarks>
public sealed class StoredFileTaggingTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _filesRoute = "/api/v1/files";

    [Fact]
    public async Task TagsSurviveTheRoundTrip_NormalisedAndDeDuplicated()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var registered = await RegisterFileAsync(owner);
        var tags = new Uri($"{_filesRoute}/{registered.Id}/tags", UriKind.Relative);

        // Three spellings of two tags. What comes back says whether the domain's normalisation ran.
        using var written = await owner.PutAsJsonAsync(
            tags,
            new ReplaceStoredFileTagsRequest(["Invoice", "invoice ", "2026"]),
            TestToken);

        written.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterWriting = await written.Content.ReadFromJsonAsync<StoredFileResponse>(TestToken);

        afterWriting.ShouldNotBeNull().Tags.ShouldBe(["invoice", "2026"], ignoreOrder: true);

        // Read back through the query projection rather than the command's own answer: the two are
        // different code paths onto the same rows, and only this one goes through the tag table.
        using var reread = await owner.GetAsync(
            new Uri($"{_filesRoute}/{registered.Id}", UriKind.Relative),
            TestToken);

        var afterReading = await reread.Content.ReadFromJsonAsync<StoredFileResponse>(TestToken);

        afterReading.ShouldNotBeNull().Tags.ShouldBe(["invoice", "2026"], ignoreOrder: true);
    }

    [Fact]
    public async Task ReplacingIsTotal_AndAnEmptySetClearsThem()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var registered = await RegisterFileAsync(owner);
        var tags = new Uri($"{_filesRoute}/{registered.Id}/tags", UriKind.Relative);

        using (var first = await owner.PutAsJsonAsync(
            tags,
            new ReplaceStoredFileTagsRequest(["keep", "drop"]),
            TestToken))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var second = await owner.PutAsJsonAsync(
            tags,
            new ReplaceStoredFileTagsRequest(["keep", "added"]),
            TestToken))
        {
            second.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                await second.Content.ReadAsStringAsync(TestToken));

            var replaced = await second.Content.ReadFromJsonAsync<StoredFileResponse>(TestToken);

            replaced.ShouldNotBeNull().Tags.ShouldBe(
                ["keep", "added"],
                ignoreOrder: true,
                "a replacement is total: what the caller left out is gone, and the row it had in the "
                + "tag table with it.");
        }

        using var cleared = await owner.PutAsJsonAsync(
            tags,
            new ReplaceStoredFileTagsRequest([]),
            TestToken);

        cleared.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await cleared.Content.ReadAsStringAsync(TestToken));

        var afterClearing = await cleared.Content.ReadFromJsonAsync<StoredFileResponse>(TestToken);

        afterClearing.ShouldNotBeNull().Tags.ShouldBeEmpty(
            "an empty list is how a caller asks for no tags, which is why the field is required and "
            + "an empty value is not a validation failure.");
    }

    [Fact]
    public async Task MoreTagsThanTheFileWillCarry_IsRefused()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var registered = await RegisterFileAsync(owner);

        string[] tooMany = [.. Enumerable
            .Range(0, StoredFile.MaxTags + 1)
            .Select(index => $"tag-{index}")];

        using var response = await owner.PutAsJsonAsync(
            new Uri($"{_filesRoute}/{registered.Id}/tags", UriKind.Relative),
            new ReplaceStoredFileTagsRequest(tooMany),
            TestToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest,
            "the cap is read from the aggregate by the validator, so a set over it is refused before "
            + "the domain has to raise for it.");
    }
    /// <summary>
    /// A registration is enough: nothing here deposits bytes, because a label is settled before
    /// anything reads content.
    /// </summary>
    private static async Task<StoredFileRegistrationResponse> RegisterFileAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri(_filesRoute, UriKind.Relative),
            new RegisterFileRequest(
                "quarterly-report.png",
                "image/png",
                SizeInBytes: 4_096,

                // Any 64 hexadecimal characters: nothing is deposited, so no digest is ever compared
                // against content. What matters is that the value is one the domain accepts.
                new string('a', 64)),
            TestToken);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Registering a file failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return await ApiJson.ReadAsync<StoredFileRegistrationResponse>(response, TestToken);
    }
}
