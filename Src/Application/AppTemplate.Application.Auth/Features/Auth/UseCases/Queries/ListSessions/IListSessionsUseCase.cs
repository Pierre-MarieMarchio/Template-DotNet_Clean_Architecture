using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.ListSessions;

/// <summary>Whole input is ambient: the caller's own id and session, taken from the request's principal.</summary>
public interface IListSessionsUseCase : IUseCase<Result<IReadOnlyList<ListSessionsItem>>>;
