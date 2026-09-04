namespace AppTemplate.Application.Features.Auth.Ports;

/// <param name="HtmlBody">Already encoded. It carries a single-use token, so it must not be logged.</param>
public sealed record ConfirmationEmail(string Subject, string HtmlBody);
