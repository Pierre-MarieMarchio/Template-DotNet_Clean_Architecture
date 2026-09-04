namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>
/// The right to deposit one file's bytes, handed to the client so it can upload straight to the
/// object store.
/// </summary>
/// <remarks>
/// <b>Every field of this is a credential or part of one.</b> The response carrying it is
/// <c>Cache-Control: no-store</c> for the same reason a token response is: whoever holds
/// <paramref name="Url"/> can write the object, with no identity attached and nothing left to check.
/// </remarks>
/// <param name="Url">Signed, single-purpose, and short-lived. Do not log it, persist it or share
/// it.</param>
/// <param name="Method">The HTTP method the signature covers. A grant signed for one method does not
/// authorise another, so the client is not left to guess.</param>
/// <param name="RequiredHeaders">
/// Headers the signature covers, which the deposit must send back verbatim. Carried as data rather
/// than described in prose, so a client follows them instead of a paragraph.
/// </param>
/// <param name="ExpiresAt">After this instant the URL is worthless. Register again rather than
/// waiting for it: an abandoned registration costs a slot that comes back.</param>
public sealed record UploadGrantResponse(
    string Url,
    string Method,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);
