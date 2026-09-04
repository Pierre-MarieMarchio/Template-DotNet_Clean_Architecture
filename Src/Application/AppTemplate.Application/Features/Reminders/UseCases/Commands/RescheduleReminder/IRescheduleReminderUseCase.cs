using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Reminders.Dtos;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;

public interface IRescheduleReminderUseCase
    : IUseCase<RescheduleReminderCommand, Result<Versioned<ReminderDto>>>;
