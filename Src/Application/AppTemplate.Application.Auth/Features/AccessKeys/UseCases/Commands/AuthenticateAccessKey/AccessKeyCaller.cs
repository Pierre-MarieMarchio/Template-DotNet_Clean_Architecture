namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.AuthenticateAccessKey;

public sealed record AccessKeyCaller(Guid UserId, Guid AccessKeyId, IReadOnlyList<string> Scopes);
