namespace AppTemplate.Api.Features.Notes.Contracts.Requests;

/// <summary>The owner is the authenticated caller and is not a field here.</summary>
/// <param name="Title">Required, at most 200 characters once trimmed.</param>
/// <param name="Body">Required but may be empty, at most 10000 characters.</param>
public sealed record CreateNoteRequest(string Title, string Body);
