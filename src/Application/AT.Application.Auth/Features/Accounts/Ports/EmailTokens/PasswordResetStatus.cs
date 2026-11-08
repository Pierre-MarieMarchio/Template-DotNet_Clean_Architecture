namespace AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;

public enum PasswordResetStatus
{
    Reset,

    InvalidToken,

    Rejected,
}
