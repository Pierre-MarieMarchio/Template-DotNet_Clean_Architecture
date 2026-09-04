using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;

namespace AppTemplate.Infrastructure.InMemory.Features.Reminders;

/// <summary>
/// An <see cref="IReminderNotifier"/> that delivers to memory and never rings anything real.
/// <para>
/// The same shape as <c>InMemoryEmailSender</c>, standing in for the same kind of real adapter:
/// <c>EmailReminderNotifier</c> (in <c>AppTemplate.Infrastructure.Email</c>) actually sends mail,
/// so a test substitutes this one instead — see <see cref="InMemoryModule.AddInMemoryReminderNotifications"/>.
/// </para>
/// <para>
/// It does not throw and does not queue, for the same reason <c>InMemoryEmailSender</c> does not: a
/// double that simulates failure modes accumulates a second implementation of the thing under test.
/// </para>
/// </summary>
internal sealed class InMemoryReminderNotifier(
    RecordedReminderNotifications recorded,
    IDateTimeProvider dateTimeProvider) : IReminderNotifier
{
    public Task NotifyAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        recorded.Record(new SentReminderNotification(
            notification.OwnerId,
            notification.TodoItemId,
            notification.DueAt,
            dateTimeProvider.UtcNow));

        return Task.CompletedTask;
    }
}
