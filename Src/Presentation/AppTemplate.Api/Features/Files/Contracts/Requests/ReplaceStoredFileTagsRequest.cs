namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>
/// The labels a file should carry afterwards.
/// </summary>
/// <param name="Tags">
/// The complete set, not an addition: anything absent from it is removed. An empty list clears the
/// file's tags, which is why the field is required and an empty value is not an error.
/// </param>
public sealed record ReplaceStoredFileTagsRequest(IReadOnlyList<string> Tags);
