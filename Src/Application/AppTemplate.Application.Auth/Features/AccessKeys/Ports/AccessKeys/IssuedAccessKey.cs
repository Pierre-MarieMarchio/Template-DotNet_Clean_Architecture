namespace AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;

public sealed record IssuedAccessKey(Guid Id, string Key, string Prefix, DateTimeOffset CreatedAt);
