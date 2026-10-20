using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.ListSessions;

public interface IListSessionsUseCase : IUseCase<Result<IReadOnlyList<ListSessionsItem>>>;
