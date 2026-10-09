namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>
/// The tags the caller has already used, wrapped in an object so the response can gain fields.
/// </summary>
/// <remarks>
/// A suggestion for a picker or filter: cached briefly, so a tag added moments ago may be missing.
/// </remarks>
/// <param name="Tags">Every tag in use, each once, ordered so two calls agree.</param>
public sealed record UsedTagsResponse(IReadOnlyList<string> Tags);
