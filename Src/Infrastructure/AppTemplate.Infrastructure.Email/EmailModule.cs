using AppTemplate.Application.Core.Common.Ports;
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
//#endif
using AppTemplate.Infrastructure.Email.Common.Http;
using AppTemplate.Infrastructure.Email.Common.Smtp;
//#if (reminders)
using AppTemplate.Infrastructure.Email.Features.Reminders;
//#endif
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Email;

public static class EmailModule
{
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
        //#if (reminders)

        services.AddScoped<IReminderNotifier, EmailReminderNotifier>();
        //#endif

        return services;
    }

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

    private static Uri SendEndpoint(PostmarkOptions options) =>
        new(options.ApiBaseUrl.EndsWith('/') ? options.ApiBaseUrl : options.ApiBaseUrl + "/");
}
