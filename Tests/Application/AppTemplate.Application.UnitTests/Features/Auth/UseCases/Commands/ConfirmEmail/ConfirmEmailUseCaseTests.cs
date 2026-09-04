using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ConfirmEmail;

public sealed class ConfirmEmailUseCaseTests
{
    private readonly IEmailConfirmationTokensService _confirmationTokens = Substitute.For<IEmailConfirmationTokensService>();
    private readonly ConfirmEmailUseCase _useCase;

    public ConfirmEmailUseCaseTests() =>
        _useCase = new ConfirmEmailUseCase(_confirmationTokens, new ConfirmEmailCommandValidator());

    /// <summary>
    /// Every way redeeming can fail. Read off the enum, so a new outcome cannot be added without a
    /// decision about how it is answered.
    /// </summary>
    public static TheoryData<EmailConfirmationStatus> Refusals =>
        [.. Enum.GetValues<EmailConfirmationStatus>()
            .Where(outcome => outcome is not EmailConfirmationStatus.Confirmed)];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// A request missing either field must be refused before the single-use token is consumed.
    /// Removing the <c>IsValid</c> check turns this red.
    /// </summary>
    [Theory]
    [InlineData("", "a-token")]
    [InlineData("   ", "a-token")]
    [InlineData("someone@example.com", "")]
    [InlineData("someone@example.com", "   ")]
    public async Task AnIncompleteRequest_NeverRedeemsAnything(string email, string token)
    {
        var result = await _useCase.ExecuteAsync(new ConfirmEmailCommand(email, token), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        _confirmationTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARedeemedToken_Confirms()
    {
        GivenTheOutcomeIs(EmailConfirmationStatus.Confirmed);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBeNull();
    }

    /// <summary>
    /// An unknown address and a wrong token answer identically, down to the message: telling them
    /// apart would let anybody holding a junk token ask whether an address is registered.
    /// </summary>
    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task EveryRefusal_AnswersWithTheSameError(EmailConfirmationStatus outcome)
    {
        GivenTheOutcomeIs(outcome);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Validation(
            "auth.confirmEmail.invalid",
            "The confirmation link is invalid or has expired."));
    }

    /// <summary>
    /// The refusals compared against each other rather than against a literal, so a change that gave
    /// all of them the same *new* error would still be caught by one of the two tests.
    /// </summary>
    [Fact]
    public async Task NoRefusal_IsDistinguishableFromAnother()
    {
        var errors = new List<Error>();

        foreach (var outcome in Enum.GetValues<EmailConfirmationStatus>())
        {
            if (outcome is EmailConfirmationStatus.Confirmed)
            {
                continue;
            }

            GivenTheOutcomeIs(outcome);

            var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

            errors.Add(result.Error.ShouldNotBeNull());
        }

        errors.Count.ShouldBeGreaterThan(1, "there is nothing to compare, so this proves nothing.");
        errors.Distinct().Count().ShouldBe(1, "the refusals differ, and the difference is measurable.");
    }

    [Fact]
    public async Task TheAddressAndTheToken_AreForwardedAsGiven()
    {
        GivenTheOutcomeIs(EmailConfirmationStatus.Confirmed);
        using var cancellation = new CancellationTokenSource();

        await _useCase.ExecuteAsync(AValidRequest(), cancellation.Token);

        await _confirmationTokens.Received(1).RedeemAsync(
            "someone@example.com",
            "a-token",
            cancellation.Token);
    }

    private static ConfirmEmailCommand AValidRequest() => new("someone@example.com", "a-token");

    private void GivenTheOutcomeIs(EmailConfirmationStatus outcome) =>
        _confirmationTokens.RedeemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(outcome);
}
