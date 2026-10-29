using AppTemplate.Application.Auth.Features.AccessKeys.Model.Errors;
using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.AuthenticateAccessKey;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace AppTemplate.PresentationAppMcp.UnitTests.TestSupport;

internal static class TestKeys
{
    internal const string ReadWrite = "ak_read-write";

    internal const string ReadOnly = "ak_read-only";

    internal const string Another = "ak_another-owner";

    internal const string Revoked = "ak_revoked";

    internal static Guid Owner { get; } = Guid.CreateVersion7();

    internal static Guid AnotherOwner { get; } = Guid.CreateVersion7();

    internal static void Trust(IServiceCollection services)
    {
        var accessKeys = Substitute.For<IAuthenticateAccessKeyUseCase>();
        accessKeys.ExecuteAsync(Arg.Any<AuthenticateAccessKeyCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<AuthenticateAccessKeyCommand>().Key switch
            {
                ReadWrite => Result.Success(new AccessKeyCaller(Owner, Guid.CreateVersion7(), AccessKeyPolicy.Scopes)),
                ReadOnly => Result.Success(new AccessKeyCaller(Owner, Guid.CreateVersion7(), [AccessKeyPolicy.ReadScope])),
                Another => Result.Success(new AccessKeyCaller(AnotherOwner, Guid.CreateVersion7(), AccessKeyPolicy.Scopes)),
                _ => Result.Failure<AccessKeyCaller>(AccessKeysErrors.InvalidKey),
            });

        services.RemoveAll<IAuthenticateAccessKeyUseCase>();
        services.AddSingleton(accessKeys);
    }
}
