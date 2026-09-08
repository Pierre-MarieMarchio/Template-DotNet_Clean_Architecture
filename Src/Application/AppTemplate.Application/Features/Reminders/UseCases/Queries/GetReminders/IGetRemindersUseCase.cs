using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.Reminders.Dtos;

namespace AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;

public interface IGetRemindersUseCase : IUseCase<GetRemindersQuery, Result<IReadOnlyList<ReminderDto>>>;
