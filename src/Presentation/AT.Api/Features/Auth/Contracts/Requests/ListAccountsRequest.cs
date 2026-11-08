namespace AppTemplate.Api.Features.Auth.Contracts.Requests;

/// <summary>The filter and paging of the account list.</summary>
/// <param name="Email">Keeps the accounts whose address starts with it, ignoring case.</param>
/// <param name="Page">The page to read, from 1.</param>
/// <param name="PageSize">The number of accounts per page.</param>
public sealed record ListAccountsRequest(string? Email, int? Page, int? PageSize);
