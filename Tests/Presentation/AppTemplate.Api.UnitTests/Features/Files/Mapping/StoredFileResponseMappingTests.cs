using System.Text.Json;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.Features.Files.Mapping;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Files.Mapping;

/// <summary>
/// A hand-written mapper's only failure mode is a field nobody copied, so every test here asserts on
/// the whole shape rather than on the members that happen to be interesting.
/// </summary>
public sealed class StoredFileResponseMappingTests
{
    private static readonly DateTimeOffset _registeredAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private static readonly DateTimeOffset _availableAt = new(2026, 3, 4, 5, 9, 7, TimeSpan.Zero);

    /// <summary>What MVC serialises with when nothing overrides it, which is the case here.</summary>
    private static readonly JsonSerializerOptions _mvcDefaults = new JsonOptions().JsonSerializerOptions;

    /// <summary>The one difference that would break every upload, so that asserting it is absent means something.</summary>
    private static readonly JsonSerializerOptions _camelCasedKeys =
        new(JsonSerializerDefaults.Web) { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void ToResponse_File_CopiesEveryField()
    {
        var file = AFile(StoredFileState.Available, _availableAt);

        var response = StoredFileResponseMapping.ToResponse(file);

        response.Id.ShouldBe(file.Id);
        response.Name.ShouldBe(file.Name);
        response.DeclaredMediaType.ShouldBe(file.DeclaredMediaType);
        response.SizeInBytes.ShouldBe(file.SizeInBytes);
        response.Checksum.ShouldBe(file.Checksum);
        response.Status.ShouldBe("available");
        response.RegisteredAt.ShouldBe(file.RegisteredAt);
        response.AvailableAt.ShouldBe(_availableAt);
    }

    [Fact]
    public void ToResponse_File_CarriesAnUnconfirmedDepositAsUnconfirmed()
    {
        var response = StoredFileResponseMapping.ToResponse(AFile(StoredFileState.Pending, availableAt: null));

        response.Status.ShouldBe("pending");
        response.AvailableAt.ShouldBeNull();
    }

    /// <summary>
    /// The two words are the same ones <c>StoredFileFilter.Create</c> accepts for <c>state</c>, which
    /// is what lets a client filter with a value it just read instead of translating between two
    /// vocabularies. A rename on either side without the other is what this holds.
    /// </summary>
    [Theory]
    [InlineData(StoredFileState.Pending, "pending")]
    [InlineData(StoredFileState.Available, "available")]
    public void ToResponse_File_NamesTheStateInTheFiltersOwnVocabulary(StoredFileState state, string expected)
    {
        var file = AFile(state, state == StoredFileState.Available ? _availableAt : null);

        StoredFileResponseMapping.ToResponse(file).Status.ShouldBe(expected);
    }

    /// <summary>
    /// A member added to <see cref="StoredFileState"/> without a word for it here would otherwise be
    /// served as whatever the last arm happened to return.
    /// </summary>
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

    /// <summary>
    /// The store checks the deposit against a signature that covers these header names, so a client
    /// has to send them back exactly as written. Nothing in this API configures
    /// <see cref="JsonSerializerOptions.DictionaryKeyPolicy"/>, which is what leaves them alone —
    /// and a deployment that set one to match the camel-cased property names would break every
    /// upload without breaking anything a test was watching.
    /// </summary>
    /// <remarks>
    /// Serialised through <see cref="JsonOptions"/>'s own defaults rather than options this test
    /// picked, because the guarantee is about what MVC does, not about what a policy named here
    /// would do.
    /// <para>
    /// Asserted on the parsed document rather than on the JSON text: Shouldly's string
    /// <c>ShouldContain</c> compares case-insensitively, so a text assertion here would pass against
    /// exactly the camel-cased output it is written to reject. The second half below is what proves
    /// this one does not.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Every projection here is written as a <c>Map</c>, which must leave a failure alone rather than
    /// evaluating a value a failed result never produced.
    /// </summary>
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
            "quarterly-report.pdf",
            "application/pdf",
            SizeInBytes: 1024,
            Checksum: new string('a', 64),
            state,
            _registeredAt,
            availableAt);

    private static IssuedUploadGrant AGrant() =>
        new(
            "https://store.example/put?sig=abc",
            "PUT",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "application/pdf" },
            _registeredAt.AddMinutes(30));

    /// <summary>
    /// Every state has a word on the wire, checked against the enum rather than against a list
    /// somebody kept in step.
    /// </summary>
    /// <remarks>
    /// The mapping is an exhaustive switch that throws on anything it does not name, so a state
    /// added to the domain without a word here is a 500 on the first read of a file in it — and no
    /// test built around a file this suite constructs would see it, because those files are never
    /// in the new state. That is the defect CONTRIBUTING.md records against a switch over an event
    /// enum, arriving at the same place by the same route.
    /// <para>
    /// It also pins the words to what <c>StoredFileFilter</c> accepts. A client must be able to
    /// filter by the value it just read, and the two lists live in different projects.
    /// </para>
    /// </remarks>
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
                "photo.png",
                "image/png",
                12,
                new string('a', 64),
                state,
                DateTimeOffset.UtcNow,
                null);

            var response = Should.NotThrow(
                () => StoredFileResponseMapping.ToResponse(dto),
                $"{state} has no word on the wire, so reading a file in that state answers 500.");

            response.Status.ShouldNotBeNullOrWhiteSpace();
        }
    }
}
