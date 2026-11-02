namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Queries.ListAccounts;

public sealed record ListAccountsQuery(string? Email, int? Page, int? PageSize);
