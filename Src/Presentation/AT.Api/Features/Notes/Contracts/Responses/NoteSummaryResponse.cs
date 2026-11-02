namespace AppTemplate.Api.Features.Notes.Contracts.Responses;

/// <summary>The wire shape of one note inside a page, without its body.</summary>
public sealed record NoteSummaryResponse(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt);
