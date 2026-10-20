using System.Text.Json;
using AppTemplate.Api.Features.Files.Contracts.Mapping;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Files.Contracts.Mapping;

public sealed class StoredFileResponseMappingTests
{
    private static readonly DateTimeOffset _registeredAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private static readonly DateTimeOffset _availableAt = new(2026, 3, 4, 5, 9, 7, TimeSpan.Zero);

    private static readonly JsonSerializerOptions _mvcDefaults = new JsonOptions().JsonSerializerOptions;

    private static readonly JsonSerializerOptions _camelCasedKeys =
        new(JsonSerializerDefaults.Web) { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void ToResponse_File_CopiesEveryField()
    {
        var file = AFile(StoredFileState.Available, _availableAt);

        var response = StoredFileResponseMapping.ToResponse(file);

        response.Id.ShouldBe(file.Id);
        response.Category.ShouldBe(file.Category);
        response.Name.ShouldBe(file.Name);
        response.DeclaredMediaType.ShouldBe(file.DeclaredMediaType);
        response.SizeInBytes.ShouldBe(file.SizeInBytes);
        response.Checksum.ShouldBe(file.Checksum);
        response.Status.ShouldBe("available");
        response.RegisteredAt.ShouldBe(file.RegisteredAt);
        response.AvailableAt.ShouldBe(_availableAt);
    }

    [Fact]
    public void TheWireShape_CarriesTheCategory()
    {
        var response = StoredFileResponseMapping.ToResponse(AFile(StoredFileState.Available, _availableAt));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, _mvcDefaults));

        document.RootElement.GetProperty("category").GetString().ShouldBe("document");
    }

    [Fact]
    public void ToResponse_File_CarriesAnUnconfirmedDepositAsUnconfirmed()
    {
        var response = StoredFileResponseMapping.ToResponse(AFile(StoredFileState.Pending, availableAt: null));

        response.Status.ShouldBe("pending");
        response.AvailableAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(StoredFileState.Pending, "pending")]
    [InlineData(StoredFileState.Available, "available")]
    public void ToResponse_File_NamesTheStateInTheFiltersOwnVocabulary(StoredFileState state, string expected)
    {
        var file = AFile(state, state == StoredFileState.Available ? _availableAt : null);

        StoredFileResponseMapping.ToResponse(file).Status.ShouldBe(expected);
    }

    [Fact]
    public void ToResponse_File_RefusesAStateItHasNoWordFor()
    {
        var file = AFile((StoredFileState)42, availableAt: null);

        Should.Throw<ArgumentOutOfRangeException>(() => StoredFileResponseMapping.ToResponse(file));
    }

    [Fact]
    public void ToResponse_Grant_CopiesEveryField()
    {
        var expiresAt = _registeredAt.AddMinutes(30);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Content-Type"] = "application/pdf",
            ["Content-Length"] = "1024",
        };

        var response = StoredFileResponseMapping.ToResponse(
            new IssuedUploadGrant("https://store.example/put?sig=abc", "PUT", headers, expiresAt));

        response.Url.ShouldBe("https://store.example/put?sig=abc");
        response.Method.ShouldBe("PUT");
        response.RequiredHeaders.ShouldBe(headers);
        response.ExpiresAt.ShouldBe(expiresAt);
    }

    [Fact]
    public void TheGrantsRequiredHeaders_AreServedVerbatim()
    {
        var response = StoredFileResponseMapping.ToResponse(new IssuedUploadGrant(
            "https://store.example/put?sig=abc",
            "PUT",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "application/pdf" },
            _registeredAt));

        HeaderNamesIn(response, _mvcDefaults).ShouldBe(["Content-Type"]);

        HeaderNamesIn(response, _camelCasedKeys).ShouldBe(
            ["content-Type"],
            "a key policy does change these names, so the assertion above is distinguishing MVC's "
            + "defaults from the one configuration that would break every upload.");
    }

    [Fact]
    public void ToResponse_Registration_CarriesTheIdAndTheGrant()
    {
        var storedFileId = Guid.CreateVersion7();
        var grant = AGrant();

        var response = StoredFileResponseMapping.ToResponse(new RegisterFileOutcome(storedFileId, grant));

        response.Id.ShouldBe(storedFileId);
        response.Upload.Url.ShouldBe(grant.Url);
        response.Upload.Method.ShouldBe(grant.Method);
        response.Upload.ExpiresAt.ShouldBe(grant.ExpiresAt);
    }

    [Fact]
    public void ToPageResponse_CarriesTheMetadataAndTheItems()
    {
        var page = PagedResult.Offset<StoredFileDto>(
            [AFile(StoredFileState.Available, _availableAt)],
            page: 2,
            pageSize: 20,
            totalCount: 41);

        var result = StoredFileResponseMapping.ToPageResponse(page);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Single().Status.ShouldBe("available");
        result.Value.Page.ShouldBe(2);
        result.Value.PageSize.ShouldBe(20);
        result.Value.TotalCount.ShouldBe(41);
        result.Value.TotalPages.ShouldBe(3);
        result.Value.HasNextPage.ShouldBeTrue();
        result.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public void ToFileResponse_KeepsTheVersionBesideTheRepresentation()
    {
        var file = AFile(StoredFileState.Pending, availableAt: null);

        var result = StoredFileResponseMapping.ToFileResponse(new Versioned<StoredFileDto>(file, 4321u));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(4321u);
        result.Value.Value.Id.ShouldBe(file.Id);
    }

    [Fact]
    public void EveryProjection_PassesAFailureThroughUntouched()
    {
        var error = Error.Conflict("storedFile.quotaExceeded", "No room.");

        StoredFileResponseMapping.ToPageResponse(Result.Failure<PagedResult<StoredFileDto>>(error))
            .Error.ShouldBe(error);
        StoredFileResponseMapping.ToFileResponse(Result.Failure<Versioned<StoredFileDto>>(error))
            .Error.ShouldBe(error);
        StoredFileResponseMapping.ToRegistrationResponse(Result.Failure<RegisterFileOutcome>(error))
            .Error.ShouldBe(error);
    }

    private static IReadOnlyList<string> HeaderNamesIn(UploadGrantResponse response, JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, options));

        return
        [
            .. document.RootElement
                .GetProperty("requiredHeaders")
                .EnumerateObject()
                .Select(header => header.Name),
        ];
    }

    private static StoredFileDto AFile(StoredFileState state, DateTimeOffset? availableAt) =>
        new(
            Guid.CreateVersion7(),
            "document",
            "quarterly-report.pdf",
            "application/pdf",
            SizeInBytes: 1024,
            Checksum: new string('a', 64),
            state,
            _registeredAt,
            availableAt,
            Tags: ["invoice", "2026"]);

    private static IssuedUploadGrant AGrant() =>
        new(
            "https://store.example/put?sig=abc",
            "PUT",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "application/pdf" },
            _registeredAt.AddMinutes(30));

    [Fact]
    public void EveryState_HasAWordOnTheWire()
    {
        var states = Enum.GetValues<StoredFileState>();

        states.Length.ShouldBeGreaterThanOrEqualTo(
            4,
            "Fewer states were found than the aggregate declares, so this is reading the wrong enum.");

        foreach (var state in states)
        {
            var dto = new StoredFileDto(
                Guid.CreateVersion7(),
                "image",
                "photo.png",
                "image/png",
                12,
                new string('a', 64),
                state,
                DateTimeOffset.UtcNow,
                null, []);

            var response = Should.NotThrow(
                () => StoredFileResponseMapping.ToResponse(dto),
                $"{state} has no word on the wire, so reading a file in that state answers 500.");

            response.Status.ShouldNotBeNullOrWhiteSpace();
        }
    }
}
