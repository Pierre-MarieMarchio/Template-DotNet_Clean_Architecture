using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

public interface ICancelReminderUseCase : IUseCase<CancelReminderCommand, Result>;
