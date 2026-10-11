namespace AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;

/// <summary>
/// What a session records about the client that started or last extended it, so its owner can
/// tell one session from another in a list. A label, never a credential: nothing is decided on it.
/// </summary>
/// <param name="UserAgent">The client's <c>User-Agent</c> header as sent, or <c>null</c> when it sent
/// none. The adapter shortens it to what its column holds.</param>
public sealed record SessionClient(string? UserAgent)
{
    /// <summary>A client that said nothing about itself.</summary>
    public static SessionClient Unknown { get; } = new((string?)null);
}
