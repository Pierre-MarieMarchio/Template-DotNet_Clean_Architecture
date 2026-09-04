using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;

/// <summary>No command: everything this needs is ambient — the clock, and every owner's due
/// reminders — which is also why it must never read <c>ICurrentUser</c>. See
/// <see cref="FireDueRemindersUseCase"/>.</summary>
public interface IFireDueRemindersUseCase : IUseCase<Result<int>>;
