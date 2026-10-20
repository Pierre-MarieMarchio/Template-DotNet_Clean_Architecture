using AppTemplate.Api.Features.ExternalIdentity.Composition;
using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.ExternalIdentity.Composition;

public sealed class ExternalIdentityUserAccountDirectoryTests
{
    private static readonly Guid _ownerId = Guid.CreateVersion7();

    private readonly IExternalIdentityService _externalIdentities = Substitute.For<IExternalIdentityService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOwnerStillMappedByAProvider_IsAnAccount()
    {
        _externalIdentities.FindProfileAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(new ExternalIdentityProfile("a@example.com", "Ada"));

        var exists = await new ExternalIdentityUserAccountDirectory(_externalIdentities)
            .ExistsAsync(UserId.Create(_ownerId), TestToken);

        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task AnOwnerNoMappingNames_IsNotAnAccount()
    {
        _externalIdentities.FindProfileAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns((ExternalIdentityProfile?)null);

        var exists = await new ExternalIdentityUserAccountDirectory(_externalIdentities)
            .ExistsAsync(UserId.Create(_ownerId), TestToken);

        exists.ShouldBeFalse();
    }
}
