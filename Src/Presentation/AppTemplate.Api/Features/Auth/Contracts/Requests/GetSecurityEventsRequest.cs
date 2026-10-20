namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>The paging of a security trail.</summary>
public sealed record GetSecurityEventsRequest(int? Page, int? PageSize);
