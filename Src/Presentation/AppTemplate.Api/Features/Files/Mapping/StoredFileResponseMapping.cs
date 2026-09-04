using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Api.Features.Files.Mapping;

/// <summary>
/// Projects the feature's application DTOs onto its wire contracts, by hand — for the reason
/// <see cref="AppTemplate.Api.Features.TodoLists.Mapping.TodoListResponseMapping"/> gives:
/// positional records plus <c>TreatWarningsAsErrors</c> make a member added on either side fail the
/// build here.
/// </summary>
/// <remarks>
/// Two of the projections below are the boundary earning its keep rather than ceremony.
/// <see cref="ToStatus"/> turns <see cref="StoredFileState"/> into the same two words
/// <c>StoredFileFilter</c> parses on the way in, so a client filters with the value it just read.
/// And <see cref="ToResponse(RegisterFileOutcome)"/> republishes the port's grant under a contract
/// of this layer's own, so that a field added to <see cref="IssuedUploadGrant"/> — by whoever next
/// writes an object-store adapter — is not published to every client by that edit alone.
/// </remarks>
internal static class StoredFileResponseMapping
{
    public static StoredFileResponse ToResponse(StoredFileDto file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new StoredFileResponse(
            file.Id,
            file.Name,
            file.DeclaredMediaType,
            file.SizeInBytes,
            file.Checksum,
            ToStatus(file.State),
            file.RegisteredAt,
            file.AvailableAt);
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

    /// <summary>
    /// A string on the wire rather than the domain enum's numeric default, on the same terms as
    /// <c>ReminderResponseMapping</c>: the API's contract is its own, not a view of
    /// <see cref="StoredFileState"/>.
    /// </summary>
    /// <remarks>
    /// Every member is named, and the throwing arm is what makes that a requirement rather than a
    /// habit. This repository has already paid for the alternative once: an exhaustive switch over
    /// an event enum threw on a newly added member and turned the first real call into a 500, with
    /// every unit test around it green because they had substituted the collaborator away. Adding a
    /// state to <see cref="StoredFileState"/> without adding a word here does exactly that to a
    /// caller reading a file.
    /// <para>
    /// The words themselves are the contract, and they must match what <c>StoredFileFilter</c>
    /// parses — a client has to be able to filter by the value it just read.
    /// </para>
    /// </remarks>
    private static string ToStatus(StoredFileState state) => state switch
    {
        StoredFileState.Pending => "pending",
        StoredFileState.Deposited => "deposited",
        StoredFileState.Available => "available",
        StoredFileState.Quarantined => "quarantined",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown stored file state."),
    };
}
