using CA.Application.Commons.Interfaces.UseCase;
using CA.Application.Modules.Auth.DTOs.Requests;
using CA.Application.Modules.Auth.DTOs.Response;

namespace CA.Application.Modules.Auth.Interfaces.UseCases;

public interface IAuthenticateUseCase : IBaseUseCase<AuthenticateRequest, AuthenticateResponse>
{
}
