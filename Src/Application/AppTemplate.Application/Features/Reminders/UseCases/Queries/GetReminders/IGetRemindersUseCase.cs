using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Reminders.Dtos;

namespace AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;

public interface IGetRemindersUseCase : IUseCase<GetRemindersQuery, Result<IReadOnlyList<ReminderDto>>>;
