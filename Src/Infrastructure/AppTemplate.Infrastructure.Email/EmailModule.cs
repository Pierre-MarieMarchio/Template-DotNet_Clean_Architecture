using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Infrastructure.Email.Common.Http;
using AppTemplate.Infrastructure.Email.Common.Smtp;
using AppTemplate.Infrastructure.Email.Features.Reminders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Email;

/// <summary>
/// Composes outbound email: the module's own options, and exactly one of the two transports behind
/// <see cref="IEmailSender"/>.
/// <para>
/// This module has exactly one reason to change — how mail leaves the process. It is separate
/// from the identity module, so a change of SMTP library never touches the assembly that also
/// owns password policy and token signing.
/// </para>
/// <para>
/// Only the chosen transport's options are bound, so a deployment sending over HTTP owes no relay
/// settings and one sending over SMTP owes no API credential. Each half is validated with
/// <c>ValidateOnStart</c>, which is what makes a misconfigured transport stop the process rather
/// than surface on the first registration attempt.
/// </para>
/// </summary>
public static class EmailModule
{
    /// <summary>
    /// Registers the configured transport behind <see cref="IEmailSender"/>.
    /// </summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="configuration">Must supply the <c>Email</c> section, and the <c>Postmark</c>
    /// section when <c>Email:Transport</c> names that transport.</param>
    /// <exception cref="InvalidOperationException"><c>Email:Transport</c> names a transport this
    /// module does not implement. Composition fails rather than falling back: a silent fallback
    /// would send a deployment's mail over a transport nobody asked for, and a typo in that key is
    /// exactly how it would happen.</exception>
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

        AddTransport(services, configuration);

        services.AddScoped<IReminderNotifier, EmailReminderNotifier>();

        return services;
    }

    /// <summary>
    /// The transport is read here rather than from <c>IOptions</c> because it decides what gets
    /// registered, and registration happens before any provider exists to resolve options from.
    /// An absent key means SMTP, which is what every existing deployment of this template runs.
    /// </summary>
    private static void AddTransport(IServiceCollection services, IConfiguration configuration)
    {
        string? configured = configuration[$"{EmailOptions.SectionName}:Transport"];

        string transport = string.IsNullOrWhiteSpace(configured)
            ? EmailOptions.SmtpTransport
            : configured.Trim();

        if (string.Equals(transport, EmailOptions.SmtpTransport, StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IEmailSender, MailKitEmailSender>();

            return;
        }

        if (string.Equals(transport, EmailOptions.PostmarkTransport, StringComparison.OrdinalIgnoreCase))
        {
            AddPostmarkTransport(services, configuration);

            return;
        }

        throw new InvalidOperationException(
            $"'{EmailOptions.SectionName}:Transport' is '{transport}', which names no transport this " +
            $"module implements. Use '{EmailOptions.SmtpTransport}' or " +
            $"'{EmailOptions.PostmarkTransport}'.");
    }

    /// <summary>
    /// <c>AddHttpClient</c> is what puts <see cref="PostmarkEmailSender"/> inside the outbound budget
    /// each host installs on <c>IHttpClientFactory</c>'s defaults (<c>Common/Outbound/</c>): timeouts,
    /// a circuit breaker, a concurrency bound, and retry on the safe verbs only — which a send is
    /// not, deliberately.
    /// <para>
    /// The client is named after the adapter rather than after <see cref="IEmailSender"/>, which is
    /// what the two-type-argument overload would have used: the name is what appears in the
    /// <c>System.Net.Http.HttpClient.*</c> log categories and in the resilience metrics, and an
    /// interface name there says which port failed but not which transport.
    /// </para>
    /// <para>
    /// There is deliberately no <c>RedactLoggedHeaders</c> call. The factory's logging handler
    /// redacts <em>every</em> header value it writes unless it is handed a list, and that call
    /// replaces the default rather than adding to it — so naming the server token here would be the
    /// one thing that stops the other headers from being redacted, while changing nothing about the
    /// token. The default is already the strict one.
    /// </para>
    /// </summary>
    private static void AddPostmarkTransport(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PostmarkOptions>()
            .Bind(configuration.GetSection(PostmarkOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PostmarkOptions>, PostmarkOptionsValidator>();

        services.AddHttpClient<IEmailSender, PostmarkEmailSender>(
            nameof(PostmarkEmailSender),
            (serviceProvider, client) =>
                client.BaseAddress = SendEndpoint(
                    serviceProvider.GetRequiredService<IOptions<PostmarkOptions>>().Value));
    }

    /// <summary>
    /// A base address whose path does not end in a slash loses its last segment when a relative URI
    /// is resolved against it, so an operator writing <c>https://proxy/postmark</c> would have every
    /// send addressed to <c>https://proxy/email</c> — a working configuration turned into a 404 by a
    /// character.
    /// </summary>
    private static Uri SendEndpoint(PostmarkOptions options) =>
        new(options.ApiBaseUrl.EndsWith('/') ? options.ApiBaseUrl : options.ApiBaseUrl + "/");
}
