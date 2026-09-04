using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

public interface ICancelReminderUseCase : IUseCase<CancelReminderCommand, Result>;
