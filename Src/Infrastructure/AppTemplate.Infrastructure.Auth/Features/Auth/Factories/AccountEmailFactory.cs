using AppTemplate.Application.Auth.Features.Auth.Ports.AccountEmails;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using AppTemplate.Infrastructure.Core.Common.Templating;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Factories;

/// <summary>
/// Renders the three account mails and hands them back for the caller to deliver. What each names
/// is all that distinguishes it: its template, its placeholders and the page its link points at —
/// see <see cref="EmailTemplate"/> for the encoding those go through. Each page address is
/// guaranteed by its options validator at startup.
/// </summary>
internal sealed class AccountEmailFactory(
    IOptions<EmailConfirmationOptions> confirmationOptions,
    IOptions<PasswordResetOptions> passwordResetOptions,
    IOptions<EmailChangeOptions> emailChangeOptions) : IAccountEmailFactory
{
    private static readonly EmailTemplate _confirmationTemplate = new(
        typeof(AccountEmailFactory).Assembly,
        "RegisterEmailTemplate");

    private static readonly EmailTemplate _passwordResetTemplate = new(
        typeof(AccountEmailFactory).Assembly,
        "PasswordResetEmailTemplate");

    private static readonly EmailTemplate _emailChangeTemplate = new(
        typeof(AccountEmailFactory).Assembly,
        "EmailChangeEmailTemplate");

    public Task<AccountEmail> CreateConfirmationAsync(
        string userName,
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        var page = confirmationOptions.Value.ConfirmEmailUrl
            ?? throw new InvalidOperationException(
                $"'{EmailConfirmationOptions.SectionName}:ConfirmEmailUrl' is not configured.");

        return RenderAsync(_confirmationTemplate, userName, "ConfirmationLink", page, email, token, cancellationToken);
    }

    public Task<AccountEmail> CreatePasswordResetAsync(
        string userName,
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        var page = passwordResetOptions.Value.ResetPasswordUrl
            ?? throw new InvalidOperationException(
                $"'{PasswordResetOptions.SectionName}:ResetPasswordUrl' is not configured.");

        return RenderAsync(_passwordResetTemplate, userName, "ResetLink", page, email, token, cancellationToken);
    }

    public Task<AccountEmail> CreateEmailChangeAsync(
        string userName,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default)
    {
        var page = emailChangeOptions.Value.ConfirmEmailChangeUrl
            ?? throw new InvalidOperationException(
                $"'{EmailChangeOptions.SectionName}:ConfirmEmailChangeUrl' is not configured.");

        return RenderAsync(_emailChangeTemplate, userName, "ConfirmationLink", page, newEmail, token, cancellationToken);
    }

    private static Task<AccountEmail> RenderAsync(
        EmailTemplate template,
        string userName,
        string linkPlaceholder,
        Uri page,
        string email,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rendered = template.Render(
            CurrentLanguage.Current,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = userName,
                [linkPlaceholder] = EmailLinkFactory.Create(page, email, token),
            });

        return Task.FromResult(new AccountEmail(rendered.Subject, rendered.Body));
    }
}
