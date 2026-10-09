namespace AppTemplate.Api.Features.Notes.Contracts.Requests;

/// <summary>Carries no note id: that travels in the route, so the two cannot disagree.</summary>
/// <param name="Title">Required, at most 200 characters once trimmed.</param>
/// <param name="Body">Required but may be empty, at most 10000 characters.</param>
public sealed record UpdateNoteRequest(string Title, string Body);
