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

/// <summary>
/// What the access token says about the session it was issued in, proven by reading a real token
/// back rather than by inspecting the code that wrote it.
/// </summary>
/// <remarks>
/// The claim's short name is load-bearing: the host reads <c>sid</c> back as written, because no
/// inbound claim-type mapping renames it — a token that arrived as <c>sid</c> and was answered as
/// something else would quietly turn every session into "unknown", and every revoke-on-change
/// would revoke the caller's own session too.
/// </remarks>
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

    /// <summary>
    /// The claim's value is what the host's <c>Guid.TryParse</c> will be given, so it is asserted as
    /// a round trip: parse the string out of the token and get the session back.
    /// </summary>
    [Fact]
    public async Task TheSidClaim_ParsesBackAsTheSessionItWasIssuedIn()
    {
        var sessionId = Guid.CreateVersion7();
        GivenTheAccountHolds(ClaimTypes.NameIdentifier, _userId.ToString());

        var issued = await CreateIssuer().IssueAsync(_userId, sessionId, TestToken);

        Guid.TryParse(new JsonWebToken(issued.Value).GetClaim("sid").Value, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(sessionId);
    }

    /// <summary>
    /// The account's own claims are the issuer's to collect: a claim revoked between two issuances
    /// is gone from the next token, and the session id does not replace any of them.
    /// </summary>
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
