using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResendConfirmationEmail;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.ResendConfirmationEmail;

public sealed class ResendConfirmationEmailUseCaseTests
{
    private readonly IEmailTokensService _emailTokens = Substitute.For<IEmailTokensService>();
    private readonly IAccountEmailFactory _emailFactory = Substitute.For<IAccountEmailFactory>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ResendConfirmationEmailUseCase _useCase;

    public ResendConfirmationEmailUseCaseTests() =>
        _useCase = new ResendConfirmationEmailUseCase(
            _emailTokens,
            _emailFactory,
            _emailSender,
            new ResendConfirmationEmailCommandValidator(),
            NullLogger<ResendConfirmationEmailUseCase>.Instance);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

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
        _emailTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task APendingAccount_IsSentAFreshLink()
    {
        GivenAnAccountIsAwaitingConfirmation();

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();

        await _emailFactory.Received(1).CreateConfirmationAsync(
            "someone@example.com",
            "confirmation-token",
            Arg.Any<CancellationToken>());

        await _emailSender.Received(1).SendAsync(
            "someone@example.com",
            "Confirm your email address",
            "<html>the body</html>",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAddressWithNothingPending_SendsNothingAndStillSucceeds()
    {
        _emailTokens.IssueEmailConfirmationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _emailFactory.ReceivedCalls().ShouldBeEmpty();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task APendingAddressAndAnUnknownOne_AreAnsweredIdentically()
    {
        GivenAnAccountIsAwaitingConfirmation();
        var forPending = await _useCase.ExecuteAsync(ARequest(), TestToken);

        _emailTokens.IssueEmailConfirmationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var forUnknown = await _useCase.ExecuteAsync(
            new ResendConfirmationEmailCommand("nobody@example.com"),
            TestToken);

        forPending.IsSuccess.ShouldBeTrue();
        forUnknown.IsSuccess.ShouldBe(forPending.IsSuccess);
        forUnknown.Error.ShouldBe(forPending.Error);
    }

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

    private static ResendConfirmationEmailCommand ARequest() => new("someone@example.com");

    private void GivenAnAccountIsAwaitingConfirmation()
    {
        _emailTokens.IssueEmailConfirmationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("confirmation-token");

        _emailFactory.CreateConfirmationAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new AccountEmail("Confirm your email address", "<html>the body</html>"));
    }
}
