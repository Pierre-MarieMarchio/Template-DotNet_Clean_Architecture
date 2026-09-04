using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.ConfirmationEmailFactory;
using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ResendConfirmationEmail;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ResendConfirmationEmail;

/// <summary>
/// The resend endpoint answers the same for every address it is given. Everything else it does — or
/// does not do — happens behind that one answer.
/// </summary>
public sealed class ResendConfirmationEmailUseCaseTests
{
    private readonly IEmailConfirmationTokensService _confirmationTokens = Substitute.For<IEmailConfirmationTokensService>();
    private readonly IConfirmationEmailFactory _emailFactory = Substitute.For<IConfirmationEmailFactory>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ResendConfirmationEmailUseCase _useCase;

    public ResendConfirmationEmailUseCaseTests() =>
        _useCase = new ResendConfirmationEmailUseCase(
            _confirmationTokens,
            _emailFactory,
            _emailSender,
            new ResendConfirmationEmailCommandValidator(),
            NullLogger<ResendConfirmationEmailUseCase>.Instance);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// The recovery path is the one endpoint an unconfirmed account depends on, so a blank
    /// address must be refused rather than handed to the mailer. Removing the
    /// <c>IsValid</c> check turns this red.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankEmail_IsRefusedBeforeAnythingIsMinted(string email)
    {
        var result = await _useCase.ExecuteAsync(new ResendConfirmationEmailCommand(email), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        result.Error.Details!["email"].ShouldContain("Email is required.");
        _confirmationTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task APendingAccount_IsSentAFreshLink()
    {
        GivenAnAccountIsAwaitingConfirmation();

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();

        await _emailFactory.Received(1).CreateAsync(
            "someone",
            "someone@example.com",
            "confirmation-token",
            Arg.Any<CancellationToken>());

        await _emailSender.Received(1).SendAsync(
            "someone@example.com",
            "Confirm your email address",
            "<html>the body</html>",
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// No token means no account is awaiting confirmation — unknown address or already confirmed.
    /// Nothing is sent, and the answer is the same success as for an account that was.
    /// </summary>
    [Fact]
    public async Task AnAddressWithNothingPending_SendsNothingAndStillSucceeds()
    {
        _confirmationTokens.IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PendingConfirmation?)null);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _emailFactory.ReceivedCalls().ShouldBeEmpty();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// The two cases compared directly: an unknown address and a pending one must produce results a
    /// caller cannot tell apart. Returning a failure for either turns this red.
    /// </summary>
    [Fact]
    public async Task APendingAddressAndAnUnknownOne_AreAnsweredIdentically()
    {
        GivenAnAccountIsAwaitingConfirmation();
        var forPending = await _useCase.ExecuteAsync(ARequest(), TestToken);

        _confirmationTokens.IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PendingConfirmation?)null);
        var forUnknown = await _useCase.ExecuteAsync(
            new ResendConfirmationEmailCommand("nobody@example.com"),
            TestToken);

        forPending.IsSuccess.ShouldBeTrue();
        forUnknown.IsSuccess.ShouldBe(forPending.IsSuccess);
        forUnknown.Error.ShouldBe(forPending.Error);
    }

    /// <summary>
    /// An unreachable relay must not turn into a distinguishable answer either: a failure here would
    /// say "this address had a mail to send".
    /// </summary>
    [Fact]
    public async Task AnUnreachableRelay_StillSucceeds()
    {
        GivenAnAccountIsAwaitingConfirmation();

        _emailSender.SendAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The relay is unreachable."));

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Cancellation is not an outage, so it is not swallowed: the request is gone and there is
    /// nothing to answer.
    /// </summary>
    [Fact]
    public async Task ACancelledDelivery_Propagates()
    {
        GivenAnAccountIsAwaitingConfirmation();

        _emailSender.SendAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => _useCase.ExecuteAsync(ARequest(), TestToken));
    }

    [Fact]
    public async Task TheCancellationToken_ReachesEveryStep()
    {
        GivenAnAccountIsAwaitingConfirmation();
        using var cancellation = new CancellationTokenSource();

        await _useCase.ExecuteAsync(ARequest(), cancellation.Token);

        await _confirmationTokens.Received(1).IssueAsync("someone@example.com", cancellation.Token);

        await _emailSender.Received(1).SendAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            cancellation.Token);
    }

    private static ResendConfirmationEmailCommand ARequest() => new("someone@example.com");

    private void GivenAnAccountIsAwaitingConfirmation()
    {
        _confirmationTokens.IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PendingConfirmation("someone", "confirmation-token"));

        _emailFactory.CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new ConfirmationEmail("Confirm your email address", "<html>the body</html>"));
    }
}
