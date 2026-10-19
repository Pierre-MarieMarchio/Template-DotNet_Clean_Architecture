namespace AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;

/// <summary>A caller as a validated provider token presents them: who issued the token, the subject it names, and the profile it carries, if any.</summary>
/// <param name="Issuer">The token's issuer, already validated against the configured authority.</param>
/// <param name="Subject">The provider's own identifier for the caller, opaque to this application.</param>
/// <param name="Email">The address the token carries, or <c>null</c>.</param>
/// <param name="DisplayName">The name the token carries, or <c>null</c>.</param>
public sealed record ExternalCallerDto(string Issuer, string Subject, string? Email, string? DisplayName);
