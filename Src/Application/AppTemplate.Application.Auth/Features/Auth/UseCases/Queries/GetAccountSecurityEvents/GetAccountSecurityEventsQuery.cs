namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.GetAccountSecurityEvents;

/// <summary>Which account's events an administrator reads, and which page of them.</summary>
public sealed record GetAccountSecurityEventsQuery(Guid UserId, int? Page, int? PageSize);
