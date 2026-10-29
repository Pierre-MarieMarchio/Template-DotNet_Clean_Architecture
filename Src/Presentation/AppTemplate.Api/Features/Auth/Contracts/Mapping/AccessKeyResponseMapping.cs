using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.CreateAccessKey;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Api.Features.Auth.Contracts.Mapping;

internal static class AccessKeyResponseMapping
{
    public static Result<CreatedAccessKeyResponse> ToCreatedAccessKeyResponse(
        Result<CreateAccessKeyOutcome> result) =>
        result.Map(created => new CreatedAccessKeyResponse(
            created.Id,
            created.Name,
            created.Prefix,
            created.Scopes,
            created.CreatedAt,
            created.ExpiresAt,
            created.Key));

    public static Result<AccessKeysResponse> ToAccessKeysResponse(
        Result<IReadOnlyList<LiveAccessKey>> result) =>
        result.Map(value => new AccessKeysResponse(
        [
            .. value.Select(accessKey => new AccessKeyResponse(
                accessKey.Id,
                accessKey.Name,
                accessKey.Prefix,
                accessKey.Scopes,
                accessKey.CreatedAt,
                accessKey.LastUsedAt,
                accessKey.ExpiresAt)),
        ]));
}
