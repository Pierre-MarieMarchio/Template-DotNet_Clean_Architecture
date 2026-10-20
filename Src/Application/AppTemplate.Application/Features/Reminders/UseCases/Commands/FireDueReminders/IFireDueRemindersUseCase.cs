using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;

public interface IFireDueRemindersUseCase : IUseCase<Result<int>>;
