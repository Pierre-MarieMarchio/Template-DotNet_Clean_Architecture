using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityStore;

public sealed record ExternalIdentityLink(
    string Issuer,
    string Subject,
    UserId UserId,
    string? Email,
    string? DisplayName,
    DateTimeOffset LastSeenAt);
