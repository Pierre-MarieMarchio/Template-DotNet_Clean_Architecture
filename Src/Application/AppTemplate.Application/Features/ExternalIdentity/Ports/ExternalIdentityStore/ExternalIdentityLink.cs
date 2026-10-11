using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityStore;

/// <summary>One mapping row: the provider's (issuer, subject) pair, the internal owner it stands for, and the profile last seen with it.</summary>
/// <param name="Issuer">The issuer of the tokens naming this subject.</param>
/// <param name="Subject">The provider's identifier for the caller.</param>
/// <param name="UserId">The owner id every business row of this caller carries.</param>
/// <param name="Email">The last address the provider sent, or <c>null</c>.</param>
/// <param name="DisplayName">The last name the provider sent, or <c>null</c>.</param>
/// <param name="LastSeenAt">When a token naming this subject was last accepted, to the service's granularity.</param>
public sealed record ExternalIdentityLink(
    string Issuer,
    string Subject,
    UserId UserId,
    string? Email,
    string? DisplayName,
    DateTimeOffset LastSeenAt);
