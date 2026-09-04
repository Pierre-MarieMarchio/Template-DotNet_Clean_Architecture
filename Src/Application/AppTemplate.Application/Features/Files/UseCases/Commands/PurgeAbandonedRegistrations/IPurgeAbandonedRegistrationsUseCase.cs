using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;

/// <summary>
/// No command: everything this needs is ambient — the clock, and every owner's stale registrations
/// — which is also why it must never read <c>ICurrentUser</c>. See
/// <see cref="PurgeAbandonedRegistrationsUseCase"/>. The count it answers with is how many
/// registrations were removed.
/// </summary>
public interface IPurgeAbandonedRegistrationsUseCase : IUseCase<Result<int>>;
