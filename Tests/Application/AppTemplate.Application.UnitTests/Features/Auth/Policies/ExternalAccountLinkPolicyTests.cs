using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.ExternalLogins;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.Policies;

/// <summary>
/// The three cases a first link can land in. They are asserted here rather than only through the use
/// case because the rule is the security-relevant half of external sign-in and it is a pure function:
/// every input it can receive fits in this file.
/// </summary>
public sealed class ExternalAccountLinkPolicyTests
{
    private static readonly AccountIdentity _account = new(
        Guid.CreateVersion7(),
        "someone",
        "someone@example.com",
        TwoFactorEnabled: false);

    [Fact]
    public void NoLocalAccountAtTheAddress_IsProvisioned() =>
        ExternalAccountLinkPolicy.Decide(null).ShouldBe(ExternalAccountLinkDecision.Provision);

    /// <summary>
    /// The account proved it can read mail at the address and the provider says the same about the
    /// caller. Two independent proofs of one address are what makes this the same person.
    /// </summary>
    [Fact]
    public void AConfirmedLocalAccount_IsLinked() =>
        ExternalAccountLinkPolicy.Decide(new LocalAccountMatch(_account, EmailConfirmed: true))
            .ShouldBe(ExternalAccountLinkDecision.Link);

    /// <summary>
    /// The attack this whole rule exists for: registering an address and never confirming it must not
    /// be a way to receive the account its real owner creates through their provider. Flipping the
    /// policy's guard to <c>_ =&gt; Link</c> turns this red and nothing else in the file.
    /// </summary>
    [Fact]
    public void AnUnconfirmedLocalAccount_IsRefused() =>
        ExternalAccountLinkPolicy.Decide(new LocalAccountMatch(_account, EmailConfirmed: false))
            .ShouldBe(ExternalAccountLinkDecision.Refuse);

    /// <summary>
    /// A decision the policy can never return is a branch the use case would carry for nothing, and a
    /// decision it returns that no test names is one nobody decided. Reading the enum rather than
    /// listing it means adding a fourth outcome fails here.
    /// </summary>
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
