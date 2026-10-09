namespace AppTemplate.Application.Features.Notes.Dtos;

/// <summary>
/// What a caller may write: the text, and nothing else. The id comes from the route and the owner
/// from the authenticated caller, so neither can be named in a body.
/// </summary>
public sealed record NoteContentDto(string Title, string Body);
