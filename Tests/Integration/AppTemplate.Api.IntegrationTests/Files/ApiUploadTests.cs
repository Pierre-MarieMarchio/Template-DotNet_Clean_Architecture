using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Files;

public sealed class ApiUploadTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _uploadsRoute = "/api/v1/files/uploads";
    private const string _filesRoute = "/api/v1/files";

    private static readonly byte[] _content = AText(100 * 1024);

    [Fact]
    public async Task AnUpload_StoresTheBytesAndAnswers201WithADepositedFile()
    {
        var (client, _, _) = await SignInAsync("uploader");

        using var response = await client.PostAsync(
            new Uri(_uploadsRoute, UriKind.Relative),
            AForm(_content, "notes.txt", "text/plain", name: "Meeting notes.txt"),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestToken));
        response.Headers.ETag.ShouldNotBeNull();

        var file = await ApiJson.ReadAsync<StoredFileResponse>(response, TestToken);
        file.Status.ShouldBe("deposited");
        file.Category.ShouldBe("document");
        file.Name.ShouldBe("Meeting notes.txt");
        file.DeclaredMediaType.ShouldBe("text/plain");
        file.SizeInBytes.ShouldBe(_content.Length);
        file.Checksum.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(_content)));
        response.Headers.Location!.ToString().ShouldEndWith($"{_filesRoute}/{file.Id}");

        using var read = await client.GetAsync(response.Headers.Location, TestToken);
        (await ApiJson.ReadAsync<StoredFileResponse>(read, TestToken)).Status.ShouldBe("deposited");
    }

    [Fact]
    public async Task TheBytes_AreInTheStoreUnderTheFilesDigest()
    {
        var (client, _, _) = await SignInAsync("uploader");

        using var response = await client.PostAsync(
            new Uri(_uploadsRoute, UriKind.Relative),
            AForm(_content, "notes.txt", "text/plain"),
            TestToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestToken));

        var file = await ApiJson.ReadAsync<StoredFileResponse>(response, TestToken);

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IStoredFileRepository>().GetAsync(file.Id, TestToken);
        var description = await scope.ServiceProvider.GetRequiredService<IFileContentStore>()
            .DescribeAsync(stored!.ObjectKey.Value, TestToken);

        description.ShouldNotBeNull();
        description.SizeInBytes.ShouldBe(_content.Length);
        description.Checksum.ShouldBe(file.Checksum);
    }

    [Fact]
    public async Task TheFilesName_DefaultsToThePartsFileName()
    {
        var (client, _, _) = await SignInAsync("uploader");

        using var response = await client.PostAsync(
            new Uri(_uploadsRoute, UriKind.Relative),
            AForm(_content, "quarterly.csv", "text/csv"),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestToken));
        (await ApiJson.ReadAsync<StoredFileResponse>(response, TestToken)).Name.ShouldBe("quarterly.csv");
    }

    [Fact]
    public async Task ARetriedUpload_CreatesASecondFile()
    {
        var (client, _, _) = await SignInAsync("uploader");

        using var first = await client.PostAsync(new Uri(_uploadsRoute, UriKind.Relative), AForm(_content, "a.txt", "text/plain"), TestToken);
        using var second = await client.PostAsync(new Uri(_uploadsRoute, UriKind.Relative), AForm(_content, "a.txt", "text/plain"), TestToken);

        var one = await ApiJson.ReadAsync<StoredFileResponse>(first, TestToken);
        var two = await ApiJson.ReadAsync<StoredFileResponse>(second, TestToken);
        one.Id.ShouldNotBe(two.Id);
    }

    [Fact]
    public async Task AMediaTypeTheCategoryRefuses_Answers400OnTheFile()
    {
        var (client, _, _) = await SignInAsync("uploader");

        using var response = await client.PostAsync(
            new Uri(_uploadsRoute, UriKind.Relative),
            AForm(_content, "picture.png", "image/png"),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestToken)).ShouldContain("\"file\"");
    }

    [Fact]
    public async Task AJsonBody_Answers415()
    {
        var (client, _, _) = await SignInAsync("uploader");

        using var response = await client.PostAsJsonAsync(
            new Uri(_uploadsRoute, UriKind.Relative),
            new { category = "document" },
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task TheCeiling_Is413ForTheBodyAnd400ForTheFile()
    {
        var (_, _, session) = await SignInAsync("uploader");
        using var host = Fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"{FilesOptions.SectionName}:{nameof(FilesOptions.ApiUploadMaxBytes)}",
            1024.ToString(CultureInfo.InvariantCulture)));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Tokens.AccessToken);
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "10.250.3.1");

        using var tooLarge = await client.PostAsync(
            new Uri(_uploadsRoute, UriKind.Relative),
            AForm(AText(40 * 1024), "big.txt", "text/plain"),
            TestToken);

        tooLarge.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await ApiJson.ReadProblemAsync(tooLarge, TestToken)).Code.ShouldBe("request.tooLarge");

        using var overCeiling = await client.PostAsync(
            new Uri(_uploadsRoute, UriKind.Relative),
            AForm(AText(2 * 1024), "medium.txt", "text/plain"),
            TestToken);

        overCeiling.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await overCeiling.Content.ReadAsStringAsync(TestToken)).ShouldContain("\"file\"");
    }

    private static byte[] AText(int length) =>
        [.. Enumerable.Range(0, length).Select(index => (byte)('a' + (index % 26)))];

    private static MultipartFormDataContent AForm(byte[] bytes, string fileName, string mediaType, string? name = null)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

        var form = new MultipartFormDataContent
        {
            { new StringContent("document"), "category" },
            { file, "file", fileName },
        };

        if (name is not null)
        {
            form.Add(new StringContent(name), "name");
        }

        return form;
    }
}
