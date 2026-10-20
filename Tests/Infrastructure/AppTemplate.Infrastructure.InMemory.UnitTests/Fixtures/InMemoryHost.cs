using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Fixtures;

internal static class InMemoryHost
{
    public static ServiceProvider Compose() =>
        new ServiceCollection().AddInMemoryModule().BuildServiceProvider(validateScopes: true);

    public static async Task SendAsync(
        IServiceProvider provider,
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        using var scope = provider.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<IEmailSender>()
            .SendAsync(recipient, subject, htmlBody, cancellationToken);
    }

    public static RecordedEmails MailboxOf(IServiceProvider provider) =>
        provider.GetRequiredService<RecordedEmails>();

    public static FixedDateTimeProvider ClockOf(IServiceProvider provider) =>
        provider.GetRequiredService<FixedDateTimeProvider>();
}
