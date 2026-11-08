namespace AppTemplate.Api.Features.Notes.Contracts.Responses;

/// <summary>The wire shape of one note.</summary>
public sealed record NoteResponse(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt);
