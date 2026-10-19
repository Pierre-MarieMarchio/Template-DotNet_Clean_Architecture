using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Queries.ListPasskeys;

/// <summary>Whole input is ambient: the caller's own id, taken from the request's principal.</summary>
public interface IListPasskeysUseCase : IUseCase<Result<IReadOnlyList<PasskeyCredential>>>;
