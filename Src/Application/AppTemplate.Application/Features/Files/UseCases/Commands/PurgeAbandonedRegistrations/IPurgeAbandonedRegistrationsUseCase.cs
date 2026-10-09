using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;

/// <summary>
/// One purge pass over every owner's stale registrations, so it never reads <c>ICurrentUser</c>.
/// Answers with the number of registrations removed.
/// </summary>
public interface IPurgeAbandonedRegistrationsUseCase : IUseCase<Result<int>>;
