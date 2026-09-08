using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

public interface ICancelReminderUseCase : IUseCase<CancelReminderCommand, Result>;
