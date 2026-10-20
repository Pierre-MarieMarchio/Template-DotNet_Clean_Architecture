using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;
using AppTemplate.Infrastructure.Core.Common.Templating;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;

/// <summary>
/// Renders the account mails and hands them back for the caller to deliver. What each names
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

    private static readonly EmailTemplate _alreadyRegisteredTemplate = new(
        typeof(AccountEmailFactory).Assembly,
        "AlreadyRegisteredEmailTemplate");

    public Task<AccountEmail> CreateConfirmationAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        var page = confirmationOptions.Value.ConfirmEmailUrl
            ?? throw new InvalidOperationException(
                $"'{EmailConfirmationOptions.SectionName}:ConfirmEmailUrl' is not configured.");

        return RenderAsync(_confirmationTemplate, "ConfirmationLink", page, email, token, cancellationToken);
    }

    public Task<AccountEmail> CreatePasswordResetAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        var page = passwordResetOptions.Value.ResetPasswordUrl
            ?? throw new InvalidOperationException(
                $"'{PasswordResetOptions.SectionName}:ResetPasswordUrl' is not configured.");

        return RenderAsync(_passwordResetTemplate, "ResetLink", page, email, token, cancellationToken);
    }

    public Task<AccountEmail> CreateEmailChangeAsync(
        string newEmail,
        string token,
        CancellationToken cancellationToken = default)
    {
        var page = emailChangeOptions.Value.ConfirmEmailChangeUrl
            ?? throw new InvalidOperationException(
                $"'{EmailChangeOptions.SectionName}:ConfirmEmailChangeUrl' is not configured.");

        return RenderAsync(_emailChangeTemplate, "ConfirmationLink", page, newEmail, token, cancellationToken);
    }

    public Task<AccountEmail> CreateAlreadyRegisteredAsync(string email, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rendered = _alreadyRegisteredTemplate.Render(
            CurrentLanguage.Current,
            new Dictionary<string, string>(StringComparer.Ordinal));

        return Task.FromResult(new AccountEmail(rendered.Subject, rendered.Body));
    }

    private static Task<AccountEmail> RenderAsync(
        EmailTemplate template,
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
                [linkPlaceholder] = EmailLinkFactory.Create(page, email, token),
            });

        return Task.FromResult(new AccountEmail(rendered.Subject, rendered.Body));
    }
}
