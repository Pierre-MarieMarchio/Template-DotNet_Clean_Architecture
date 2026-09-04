using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

public interface ICancelReminderUseCase : IUseCase<CancelReminderCommand, Result>;
