namespace AppTemplate.Application.Features.Auth.Ports.EmailChangeEmailComposer;

/// <param name="HtmlBody">Already encoded. It carries a single-use token, so it must not be logged.</param>
public sealed record EmailChangeEmail(string Subject, string HtmlBody);
