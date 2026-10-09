namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>The labels a file should carry afterwards.</summary>
/// <param name="Tags">The complete set: anything absent is removed, and an empty list clears the tags.</param>
public sealed record ReplaceStoredFileTagsRequest(IReadOnlyList<string> Tags);
