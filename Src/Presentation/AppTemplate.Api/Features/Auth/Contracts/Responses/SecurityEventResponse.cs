namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// One recorded security event as it reads back. <see cref="Kind"/> is the event's name rather
/// than its numeric value, so the wire keeps the same meaning when the enum gains a member in the
/// middle — the same reason the row stores it by name. <see cref="IpAddress"/> and
/// <see cref="UserAgent"/> are what the request that produced the event carried, and
/// <c>null</c> when it carried none or no request was behind it; <see cref="Detail"/> is the
/// event's one small datum — the refusal status of a failed sign-in, the role a grant or
/// revocation named.
/// </summary>
public sealed record SecurityEventResponse(
    Guid Id,
    string Kind,
    DateTimeOffset OccurredAt,
    string? IpAddress,
    string? UserAgent,
    string? Detail);
