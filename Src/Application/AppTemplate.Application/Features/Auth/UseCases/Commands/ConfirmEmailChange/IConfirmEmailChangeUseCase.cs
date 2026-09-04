using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmailChange;

/// <summary>Authenticated. See <see cref="ConfirmEmailChangeCommand"/>.</summary>
public interface IConfirmEmailChangeUseCase : IUseCase<ConfirmEmailChangeCommand, Result>;
