using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Files;

public sealed class StrictQuotaConcurrencyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _filesRoute = "/api/v1/files";
    private const string _category = "document";
    private const int _burst = 8;

    [Fact]
    public async Task ParallelRegistrations_InAStrictCategory_NeverExceedThePendingLimit()
    {
        var quota = Fixture.Factory.Services.GetRequiredService<IOptions<FilesOptions>>()
            .Value.Find(FileCategory.Create(_category))!.Quota;

        quota.IsStrict.ShouldBeTrue("the shipped document category is strict, which is what this test is about.");

        var (client, _, session) = await SignInAsync("burst");
        var owner = UserId.Create(session.UserId);

        await StorePendingAsync(owner, quota.MaxPendingUploads - 1);

        var responses = await Task.WhenAll(Enumerable.Range(0, _burst).Select(_ => RegisterAsync(client)));

        responses.Count(status => status == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(status => status == HttpStatusCode.Conflict).ShouldBe(_burst - 1);

        var usage = await UsageForAsync(owner);
        usage.PendingCount.ShouldBe(quota.MaxPendingUploads);
    }

    private static async Task<HttpStatusCode> RegisterAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri(_filesRoute, UriKind.Relative),
            new RegisterFileRequest(_category, "quarterly-report.pdf", "application/pdf", 4_096, new string('a', 64)),
            TestToken);

        return response.StatusCode;
    }

    private async Task StorePendingAsync(UserId owner, int count)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStoredFileRepository>();

        for (int index = 0; index < count; index++)
        {
            repository.Add(StoredFile.Register(
                owner,
                FileCategory.Create(_category),
                StoredFileName.Create($"earlier-{index}.pdf"),
                DeclaredMediaType.Create("application/pdf"),
                FileSize.Create(4_096),
                Sha256Checksum.Create(new string('a', Sha256Checksum.Length)),
                Fixture.Clock.UtcNow));
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken);
    }

    private async Task<OwnerStorageUsage> UsageForAsync(UserId owner)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<IStoredFileQueries>()
            .GetUsageForOwnerAsync(owner, FileCategory.Create(_category), TestToken);
    }
}
