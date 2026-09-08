using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmailChange;

/// <summary>Authenticated. See <see cref="ConfirmEmailChangeCommand"/>.</summary>
public interface IConfirmEmailChangeUseCase : IUseCase<ConfirmEmailChangeCommand, Result>;
