namespace AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;

/// <param name="HtmlBody">Already encoded. It carries a single-use token, so it must not be logged.</param>
public sealed record AccountEmail(string Subject, string HtmlBody);
