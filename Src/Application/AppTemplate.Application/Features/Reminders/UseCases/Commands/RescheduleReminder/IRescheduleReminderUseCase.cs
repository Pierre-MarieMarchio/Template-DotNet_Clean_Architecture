using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Reminders.Dtos;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;

public interface IRescheduleReminderUseCase
    : IUseCase<RescheduleReminderCommand, Result<Versioned<ReminderDto>>>;
