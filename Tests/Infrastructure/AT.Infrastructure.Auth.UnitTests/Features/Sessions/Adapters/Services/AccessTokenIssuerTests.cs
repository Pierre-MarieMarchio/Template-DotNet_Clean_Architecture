using System.Security.Claims;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Sessions.Adapters.Services;

public sealed class AccessTokenIssuerTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();
    private readonly MovableDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnIssuedToken_CarriesTheSessionItWasIssuedIn_AsTheShortSidClaim()
    {
        var sessionId = Guid.CreateVersion7();
        GivenTheAccountHolds(ClaimTypes.NameIdentifier, _userId.ToString());

        var issued = await CreateIssuer().IssueAsync(_userId, sessionId, TestToken);

        var token = new JsonWebToken(issued.Value);
        token.GetClaim(JwtRegisteredClaimNames.Sid).Value.ShouldBe(sessionId.ToString());
    }

    [Fact]
    public async Task TheSidClaim_ParsesBackAsTheSessionItWasIssuedIn()
    {
        var sessionId = Guid.CreateVersion7();
        GivenTheAccountHolds(ClaimTypes.NameIdentifier, _userId.ToString());

        var issued = await CreateIssuer().IssueAsync(_userId, sessionId, TestToken);

        Guid.TryParse(new JsonWebToken(issued.Value).GetClaim("sid").Value, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(sessionId);
    }

    [Fact]
    public async Task AnIssuedToken_StillCarriesTheAccountsOwnClaims()
    {
        GivenTheAccountHolds(ClaimTypes.NameIdentifier, _userId.ToString());

        var issued = await CreateIssuer().IssueAsync(_userId, Guid.CreateVersion7(), TestToken);

        new JsonWebToken(issued.Value).GetClaim(ClaimTypes.NameIdentifier).Value.ShouldBe(_userId.ToString());
    }

    [Fact]
    public async Task IssuingForAnAccountThatDoesNotExist_IsRefusedLoudly()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateIssuer().IssueAsync(_userId, Guid.CreateVersion7(), TestToken));
    }

    private void GivenTheAccountHolds(string claimType, string value)
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new AppUser { Id = _userId, UserName = "someone", Email = "someone@example.com" });

        _directory.GenerateClaimsAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns([new Claim(claimType, value)]);
    }

    private AccessTokenIssuer CreateIssuer() =>
        new(
            _directory,
            new OptionsWrapper<JwtOptions>(new JwtOptions
            {
                Key = "unit-tests-signing-key-0123456789-abcdefghij",
                Issuer = "https://localhost/app-template",
                Audience = "app-template-api",
                AccessTokenLifetimeInMinutes = 15,
            }),
            _clock);
}
