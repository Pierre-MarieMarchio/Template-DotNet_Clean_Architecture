namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>
/// The tags the caller has already used on their files, wrapped in an object: an array at the top
/// level can never gain a sibling field without breaking its callers.
/// </summary>
/// <remarks>
/// A suggestion for a picker or a filter, not a statement of record. It is served from a cache with
/// a short lifetime, so a tag added moments ago in another window may be missing from it.
/// </remarks>
/// <param name="Tags">Every tag in use, each once, ordered so two calls agree.</param>
public sealed record UsedTagsResponse(IReadOnlyList<string> Tags);
