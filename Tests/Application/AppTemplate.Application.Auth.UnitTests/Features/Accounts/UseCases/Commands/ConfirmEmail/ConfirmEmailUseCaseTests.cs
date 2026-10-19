using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmail;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.ConfirmEmail;

public sealed class ConfirmEmailUseCaseTests
{
    private readonly IEmailTokensService _emailTokens = Substitute.For<IEmailTokensService>();
    private readonly ConfirmEmailUseCase _useCase;

    public ConfirmEmailUseCaseTests() =>
        _useCase = new ConfirmEmailUseCase(_emailTokens, new ConfirmEmailCommandValidator());

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
        _emailTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARedeemedToken_Confirms()
    {
        GivenTheOutcomeIs(true);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBeNull();
    }

    /// <summary>
    /// An unknown address and a wrong token are one refusal from the port's point of view, so the
    /// use case has a single error to answer with: telling them apart would let anybody holding a
    /// junk token ask whether an address is registered.
    /// </summary>
    [Fact]
    public async Task ARefusedConfirmation_AnswersWithTheInvalidLinkError()
    {
        GivenTheOutcomeIs(false);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Validation(
            "auth.confirmEmail.invalid",
            "The confirmation link is invalid or has expired."));
    }

    [Fact]
    public async Task TheAddressAndTheToken_AreForwardedAsGiven()
    {
        GivenTheOutcomeIs(true);
        using var cancellation = new CancellationTokenSource();

        await _useCase.ExecuteAsync(AValidRequest(), cancellation.Token);

        await _emailTokens.Received(1).ConfirmEmailAsync(
            "someone@example.com",
            "a-token",
            cancellation.Token);
    }

    private static ConfirmEmailCommand AValidRequest() => new("someone@example.com", "a-token");

    private void GivenTheOutcomeIs(bool confirmed) =>
        _emailTokens.ConfirmEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(confirmed);
}
