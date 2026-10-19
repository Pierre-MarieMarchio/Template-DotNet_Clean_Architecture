namespace AppTemplate.Application.Features.Notes.Model.Dtos;

/// <summary>One note as its owner reads it. The version travels beside it, not inside it.</summary>
public sealed record NoteDto(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt);
