namespace AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;

/// <summary>What the provider last said about an owner: how to address them and what to call them.</summary>
/// <param name="Email">The last address the provider sent, or <c>null</c>.</param>
/// <param name="DisplayName">The last name the provider sent, or <c>null</c>.</param>
public sealed record ExternalIdentityProfile(string? Email, string? DisplayName);
