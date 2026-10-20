namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>One recorded security event as it reads back.</summary>
public sealed record SecurityEventResponse(
    Guid Id,
    string Kind,
    DateTimeOffset OccurredAt,
    string? IpAddress,
    string? UserAgent,
    string? Detail);
