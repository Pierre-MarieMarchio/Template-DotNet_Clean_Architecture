namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>
/// The right to deposit one file's bytes directly on the object store.
/// </summary>
/// <remarks>
/// Every field is a credential or part of one: whoever holds <paramref name="Url"/> can write the
/// object, so the response is <c>no-store</c>.
/// </remarks>
/// <param name="Url">Signed, single-purpose and short-lived; do not log, persist or share it.</param>
/// <param name="Method">The HTTP method the signature covers.</param>
/// <param name="RequiredHeaders">Headers the signature covers; the deposit must send them verbatim.</param>
/// <param name="ExpiresAt">After this instant the URL is worthless; register again.</param>
public sealed record UploadGrantResponse(
    string Url,
    string Method,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);
