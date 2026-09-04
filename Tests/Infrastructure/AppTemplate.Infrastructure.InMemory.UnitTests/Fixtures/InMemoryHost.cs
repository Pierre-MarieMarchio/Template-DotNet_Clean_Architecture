using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Fixtures;

/// <summary>
/// The module composed on its own, and reached the way a request reaches it: the sender reads
/// <see cref="IEmailSender"/> out of a scope, never out of the root. Scope validation is on, so a
/// lifetime mistake in the module fails here rather than in the first host that composes it.
/// </summary>
internal static class InMemoryHost
{
    public static ServiceProvider Compose() =>
        new ServiceCollection().AddInMemoryModule().BuildServiceProvider(validateScopes: true);

    /// <summary>Sends one message through the port, from its own scope.</summary>
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
