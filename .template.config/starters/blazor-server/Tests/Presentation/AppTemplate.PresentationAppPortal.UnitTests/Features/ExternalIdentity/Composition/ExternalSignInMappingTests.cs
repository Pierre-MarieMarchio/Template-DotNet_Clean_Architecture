using System.Security.Claims;
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Presentation.Core.Common.Security;
using AppTemplate.PresentationAppPortal.Features.ExternalIdentity.Composition;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppPortal.UnitTests.Features.ExternalIdentity.Composition;

public sealed class ExternalSignInMappingTests
{
    private const string _issuer = "https://idp.example.com/realms/app";

    private static readonly UserId _owner = UserId.Create(Guid.CreateVersion7());

    private readonly IExternalIdentityService _identities = Substitute.For<IExternalIdentityService>();

    [Fact]
    public async Task AProviderSubject_SignsInAsTheOwnerItMapsTo_WithTheRolesHeldLocally()
    {
        _identities.ResolveAsync(new ExternalCallerDto(_issuer, "subject-1", "someone@example.com", "Someone"), Arg.Any<CancellationToken>())
            .Returns(_owner);
        _identities.ListAssignedRolesAsync(_owner, Arg.Any<CancellationToken>()).Returns(["Admin"]);

        var principal = await Mapping(AuthenticationExternalOptions.RoleSource.Local)
            .MapAsync(IdToken(out var token), token, TestContext.Current.CancellationToken);

        principal.ShouldNotBeNull();
        principal.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(_owner.Value.ToString());
        principal.IsInRole("Admin").ShouldBeTrue();
        principal.FindFirstValue("sub").ShouldBeNull("The provider's subject stays in the mapping, never in the app's principal.");
    }

    [Fact]
    public async Task RolesFromTheToken_AreReadAtTheConfiguredPathOfTheIdToken()
    {
        _identities.ResolveAsync(Arg.Any<ExternalCallerDto>(), Arg.Any<CancellationToken>()).Returns(_owner);

        var principal = await Mapping(AuthenticationExternalOptions.RoleSource.Claim)
            .MapAsync(IdToken(out var token), token, TestContext.Current.CancellationToken);

        principal.ShouldNotBeNull();
        principal.IsInRole("app-admin").ShouldBeTrue();
        await _identities.DidNotReceiveWithAnyArgs().ListAssignedRolesAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnIdTokenWithoutASubject_MapsToNobody()
    {
        var principal = await Mapping(AuthenticationExternalOptions.RoleSource.Local)
            .MapAsync(new ClaimsPrincipal(new ClaimsIdentity()), new JsonWebToken(Token()), TestContext.Current.CancellationToken);

        principal.ShouldBeNull();
        await _identities.DidNotReceiveWithAnyArgs().ResolveAsync(default!, TestContext.Current.CancellationToken);
    }

    private ExternalSignInMapping Mapping(AuthenticationExternalOptions.RoleSource source) =>
        new(_identities, Options.Create(new AuthenticationExternalOptions
        {
            Authority = _issuer,
            Audience = "app-api",
            Roles = { Source = source, ClaimPath = "realm_access.roles" },
        }));

    private static ClaimsPrincipal IdToken(out SecurityToken token)
    {
        token = new JsonWebToken(Token());

        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "subject-1"),
                new Claim("email", "someone@example.com"),
                new Claim("email_verified", "true"),
                new Claim("name", "Someone"),
            ],
            "oidc"));
    }

    private static string Token() =>
        new JsonWebTokenHandler().CreateToken($$$"""{"iss":"{{{_issuer}}}","sub":"subject-1","realm_access":{"roles":["app-admin"]}}""");
}
