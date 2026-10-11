using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Api.Features.Files.Mapping;

/// <summary>
/// Projects the feature's application DTOs onto its wire contracts.
/// </summary>
internal static class StoredFileResponseMapping
{
    public static StoredFileResponse ToResponse(StoredFileDto file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new StoredFileResponse(
            file.Id,
            file.Category,
            file.Name,
            file.DeclaredMediaType,
            file.SizeInBytes,
            file.Checksum,
            ToStatus(file.State),
            file.RegisteredAt,
            file.AvailableAt,
            file.Tags);
    }

    public static UploadGrantResponse ToResponse(IssuedUploadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return new UploadGrantResponse(grant.Url, grant.Method, grant.RequiredHeaders, grant.ExpiresAt);
    }

    public static StoredFileRegistrationResponse ToResponse(RegisterFileOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new StoredFileRegistrationResponse(outcome.StoredFileId, ToResponse(outcome.Upload));
    }

    public static Result<PagedResponse<StoredFileResponse>> ToPageResponse(Result<PagedResult<StoredFileDto>> result) =>
        result.Map(value => PagedResponse.From(value, ToResponse));

    public static Result<Versioned<StoredFileResponse>> ToFileResponse(Result<Versioned<StoredFileDto>> result) =>
        result.Map(value => new Versioned<StoredFileResponse>(ToResponse(value.Value), value.Version));

    public static Result<StoredFileRegistrationResponse> ToRegistrationResponse(Result<RegisterFileOutcome> result) =>
        result.Map(ToResponse);

    /// <summary>A string on the wire rather than the enum's number, in the words <c>StoredFileFilter</c> parses, so a client filters with the value it just read.</summary>
    /// <remarks>Every member is named and the default arm throws, so a state added without a word here fails loudly in tests rather than as a 500 on the first read.</remarks>
    private static string ToStatus(StoredFileState state) => state switch
    {
        StoredFileState.Pending => "pending",
        StoredFileState.Deposited => "deposited",
        StoredFileState.Available => "available",
        StoredFileState.Quarantined => "quarantined",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown stored file state."),
    };

    /// <summary>The tags this caller has used on their files.</summary>
    public static Result<UsedTagsResponse> ToUsedTagsResponse(
        Result<IReadOnlyList<string>> result) =>
        result.Map(tags => new UsedTagsResponse(tags));
}
