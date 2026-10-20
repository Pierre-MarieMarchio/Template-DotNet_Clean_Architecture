using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.ExternalLogins.Model.Policies;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.ExternalLogins.Model.Policies;

public sealed class ExternalAccountLinkPolicyTests
{
    private static readonly AccountIdentity _account = new(
        Guid.CreateVersion7(),
        "someone@example.com",
        TwoFactorEnabled: false);

    [Fact]
    public void NoLocalAccountAtTheAddress_IsProvisioned() =>
        ExternalAccountLinkPolicy.Decide(null).ShouldBe(ExternalAccountLinkDecision.Provision);

    [Fact]
    public void AConfirmedLocalAccount_IsLinked() =>
        ExternalAccountLinkPolicy.Decide(new LocalAccountMatch(_account, EmailConfirmed: true))
            .ShouldBe(ExternalAccountLinkDecision.Link);

    [Fact]
    public void AnUnconfirmedLocalAccount_IsRefused() =>
        ExternalAccountLinkPolicy.Decide(new LocalAccountMatch(_account, EmailConfirmed: false))
            .ShouldBe(ExternalAccountLinkDecision.Refuse);

    [Fact]
    public void EveryDecision_IsReachable()
    {
        LocalAccountMatch?[] inputs =
        [
            null,
            new LocalAccountMatch(_account, EmailConfirmed: true),
            new LocalAccountMatch(_account, EmailConfirmed: false),
        ];

        inputs.Select(ExternalAccountLinkPolicy.Decide)
            .Distinct()
            .Order()
            .ShouldBe(Enum.GetValues<ExternalAccountLinkDecision>().Order());
    }
}
