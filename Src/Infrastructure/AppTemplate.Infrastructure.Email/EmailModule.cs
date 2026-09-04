using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Infrastructure.Email.Common.Smtp;
using AppTemplate.Infrastructure.Email.Features.Reminders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Email;

/// <summary>
/// Composes outbound email: one options type, one validator, one adapter.
/// <para>
/// This module has exactly one reason to change — how mail leaves the process. It is separate
/// from the identity module, so a change of SMTP library never touches the assembly that also
/// owns password policy and token signing.
/// </para>
/// </summary>
public static class EmailModule
{
    /// <summary>
    /// Registers the SMTP sender behind <see cref="IEmailSender"/>.
    /// </summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="configuration">Must supply the <c>Email</c> section; it is validated at
    /// start-up, so a relay that cannot be reached securely stops the process from booting
    /// rather than failing on the first registration attempt.</param>
    public static IServiceCollection AddEmailModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();

        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddScoped<IReminderNotifier, EmailReminderNotifier>();

        return services;
    }
}
