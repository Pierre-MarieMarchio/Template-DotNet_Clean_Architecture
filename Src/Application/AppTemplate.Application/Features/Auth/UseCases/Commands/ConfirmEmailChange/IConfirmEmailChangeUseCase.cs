using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmailChange;

/// <summary>Authenticated. See <see cref="ConfirmEmailChangeCommand"/>.</summary>
public interface IConfirmEmailChangeUseCase : IUseCase<ConfirmEmailChangeCommand, Result>;
