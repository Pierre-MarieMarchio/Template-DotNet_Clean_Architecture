namespace CA.Infrastructure.Identity.Interfaces.Services;

public interface IRegisterMailerService
{
    public Task SendConfirmationEmailAsync(string userName, string email, string token);

}
