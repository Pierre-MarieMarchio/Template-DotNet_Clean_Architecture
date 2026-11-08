using System.Security.Claims;
using AppTemplate.Api.Features.ExternalIdentity.Composition;
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Shouldly;
using Xunit;
using RoleSource = AppTemplate.Presentation.Core.Common.Security.AuthenticationExternalOptions.RoleSource;

namespace AppTemplate.Api.UnitTests.Features.ExternalIdentity.Composition;

public sealed class ExternalPrincipalMapperTests
{
    private const string _issuer = "https://idp.example.com/realms/app";

    private static readonly UserId _mapped = UserId.Create(Guid.CreateVersion7());

    private readonly IExternalIdentityService _identities = Substitute.For<IExternalIdentityService>();

    public ExternalPrincipalMapperTests() =>
        _identities.ResolveAsync(Arg.Any<ExternalCallerDto>(), Arg.Any<CancellationToken>()).Returns(_mapped);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMappedCaller_IsTheInternalOwner_NeverTheProvidersSubject()
    {
        string guidSubject = Guid.NewGuid().ToString();

        var principal = await Mapper().MapAsync(Principal(("sub", guidSubject)), Token(), TestToken);

        principal.ShouldNotBeNull();
        principal.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(_mapped.Value.ToString());
        principal.Identity!.IsAuthenticated.ShouldBeTrue();
        await _identities.Received(1).ResolveAsync(
            new ExternalCallerDto(_issuer, guidSubject, null, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheConfiguredClaims_SupplyTheSubjectAndTheProfile()
    {
        var options = new AuthenticationExternalOptions
        {
            SubjectClaim = "oid",
            EmailClaim = "preferred_username",
            NameClaim = "given_name",
        };

        var principal = await Mapper(options).MapAsync(
            Principal(("oid", "object-1"), ("preferred_username", "a@example.com"), ("given_name", "Ada")),
            Token(),
            TestToken);

        principal.ShouldNotBeNull();
        principal.Identity!.Name.ShouldBe("Ada");
        await _identities.Received(1).ResolveAsync(
            new ExternalCallerDto(_issuer, "object-1", "a@example.com", "Ada"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("true", "a@example.com")]
    [InlineData("false", null)]
    [InlineData(null, "a@example.com")]
    public async Task TheEmail_IsKeptUnlessTheProviderSaysItIsUnverified(string? emailVerified, string? kept)
    {
        (string Type, string Value)[] claims = emailVerified is null
            ? [("sub", "abc"), ("email", "a@example.com")]
            : [("sub", "abc"), ("email", "a@example.com"), ("email_verified", emailVerified)];

        await Mapper().MapAsync(Principal(claims), Token(), TestToken);

        await _identities.Received(1).ResolveAsync(
            new ExternalCallerDto(_issuer, "abc", kept, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATokenWithoutASubject_IsRefused_AndNothingIsMapped()
    {
        (await Mapper().MapAsync(Principal(("email", "a@example.com")), Token(), TestToken)).ShouldBeNull();

        await _identities.DidNotReceiveWithAnyArgs().ResolveAsync(default!, TestToken);
    }

    [Fact]
    public async Task ABlankSubject_IsRefused() =>
        (await Mapper().MapAsync(Principal(("sub", "  ")), Token(), TestToken)).ShouldBeNull();

    [Fact]
    public async Task ATokenWithoutAnIssuer_IsRefused() =>
        (await Mapper().MapAsync(Principal(("sub", "abc")), token: null, TestToken)).ShouldBeNull();

    [Fact]
    public async Task ASubjectTooLongToBeAKey_IsRefused() =>
        (await Mapper().MapAsync(Principal(("sub", new string('s', 256))), Token(), TestToken)).ShouldBeNull();

    [Fact]
    public async Task AStoreFailure_Propagates_RatherThanAuthenticatingAnybody()
    {
        _identities.ResolveAsync(Arg.Any<ExternalCallerDto>(), Arg.Any<CancellationToken>())
            .Returns<UserId>(_ => throw new InvalidOperationException("database down"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => Mapper().MapAsync(Principal(("sub", "abc")), Token(), TestToken));
    }

    [Fact]
    public async Task ProviderSentIdentityAndRoleClaims_AreDropped()
    {
        var principal = await Mapper().MapAsync(
            Principal(
                ("sub", "abc"),
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                (ClaimTypes.Role, "Admin")),
            Token(),
            TestToken);

        principal.ShouldNotBeNull();
        principal.FindAll(ClaimTypes.NameIdentifier).Select(claim => claim.Value).ShouldBe([_mapped.Value.ToString()]);
        principal.IsInRole("Admin").ShouldBeFalse();
        principal.FindFirstValue("sub").ShouldBe("abc");
    }

    [Fact]
    public async Task LocalRoles_AreTheAssignedOnes_WhateverTheTokenSays()
    {
        _identities.ListAssignedRolesAsync(_mapped, Arg.Any<CancellationToken>()).Returns(["Admin"]);

        var principal = await Mapper().MapAsync(
            Principal(("sub", "abc"), ("roles", "Owner")),
            Token("""{"roles":["Owner"]}"""),
            TestToken);

        principal.ShouldNotBeNull();
        principal.IsInRole("Admin").ShouldBeTrue();
        principal.IsInRole("Owner").ShouldBeFalse();
    }

    [Fact]
    public async Task ClaimRoles_AreReadFromTheConfiguredPath()
    {
        var options = new AuthenticationExternalOptions();
        options.Roles.Source = RoleSource.Claim;
        options.Roles.ClaimPath = "realm_access.roles";

        var principal = await Mapper(options).MapAsync(
            Principal(("sub", "abc")),
            Token("""{"realm_access":{"roles":["Admin","Reader"]}}"""),
            TestToken);

        principal.ShouldNotBeNull();
        principal.IsInRole("Admin").ShouldBeTrue();
        principal.IsInRole("Reader").ShouldBeTrue();
        await _identities.DidNotReceiveWithAnyArgs().ListAssignedRolesAsync(default!, TestToken);
    }

    private ExternalPrincipalMapper Mapper(AuthenticationExternalOptions? options = null) =>
        new(_identities, Microsoft.Extensions.Options.Options.Create(options ?? new AuthenticationExternalOptions()));

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(
            claims.Select(claim => new Claim(claim.Type, claim.Value)),
            authenticationType: "Bearer"));

    private static JsonWebToken Token(string extraPayload = "{}")
    {
        string payload = extraPayload.Length > 2
            ? $$"""{"iss":"{{_issuer}}",{{extraPayload[1..]}}"""
            : $$"""{"iss":"{{_issuer}}"}""";

        return new JsonWebToken(
            $"{Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""")}.{Base64UrlEncoder.Encode(payload)}.");
    }
}
