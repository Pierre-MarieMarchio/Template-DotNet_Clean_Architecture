using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Queries.ListPasskeys;

public interface IListPasskeysUseCase : IUseCase<Result<IReadOnlyList<PasskeyCredential>>>;
