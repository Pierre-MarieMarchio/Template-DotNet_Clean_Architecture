using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.Reminders.Dtos;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;

public interface IScheduleReminderUseCase : IUseCase<ScheduleReminderCommand, Result<Versioned<ReminderDto>>>;
