namespace AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;

public sealed record VerifiedExternalIdentity(
    string Provider,
    string Subject,
    string? Email,
    bool EmailVerified);
