using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteMyAccount;

/// <summary>
/// The authenticated caller deletes their own account. The authority is the re-authentication this
/// operation demands — see <c>DeleteMyAccountUseCase</c> for what that means per account.
/// </summary>
public interface IDeleteMyAccountUseCase : IUseCase<DeleteMyAccountCommand, Result>;
