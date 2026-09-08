using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.Reminders.Dtos;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;

public interface IRescheduleReminderUseCase
    : IUseCase<RescheduleReminderCommand, Result<Versioned<ReminderDto>>>;
