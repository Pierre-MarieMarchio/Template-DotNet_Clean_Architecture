namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>The tags the caller has already used on their files.</summary>
/// <param name="Tags">Every tag in use, each once, ordered so two calls agree.</param>
public sealed record UsedTagsResponse(IReadOnlyList<string> Tags);
