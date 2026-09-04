using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.EmailChangeEmailFactory;
using AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RequestEmailChange;
using AppTemplate.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.RequestEmailChange;

public sealed class RequestEmailChangeUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IEmailChangeTokensService _emailChangeTokens = Substitute.For<IEmailChangeTokensService>();
    private readonly IEmailChangeEmailFactory _emailFactory = Substitute.For<IEmailChangeEmailFactory>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RequestEmailChangeCommand("correct horse battery", "new@example.com"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _emailChangeTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "new@example.com")]
    [InlineData("old password", "not-an-email")]
    public async Task AMalformedRequest_NeverReachesTheStore(string currentPassword, string newEmail)
    {
        var result = await UseCase().ExecuteAsync(new RequestEmailChangeCommand(currentPassword, newEmail), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _emailChangeTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AWrongCurrentPassword_IsRefused()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.IncorrectCurrentPassword);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("request.validationFailed");
        result.Error.Details!["currentPassword"].ShouldContain("The current password is incorrect.");
        _emailFactory.ReceivedCalls().ShouldBeEmpty();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAvailableAddress_IsSentAConfirmationLink()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.Issued("someone", "the-token"));
        _emailFactory.CreateAsync("someone", "new@example.com", "the-token", Arg.Any<CancellationToken>())
            .Returns(new EmailChangeEmail("Confirm your new email address", "<html>the body</html>"));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();

        await _emailSender.Received(1).SendAsync(
            "new@example.com",
            "Confirm your new email address",
            "<html>the body</html>",
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The password matched, but the store has nothing to send — the address is already taken. The
    /// answer must be indistinguishable from a token that really was minted, or this endpoint tells
    /// a caller which addresses are registered.
    /// </summary>
    [Fact]
    public async Task AnAlreadyTakenAddress_SendsNothingAndStillSucceeds()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.Suppressed);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _emailFactory.ReceivedCalls().ShouldBeEmpty();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>A minted token and a suppressed one must answer identically once the password matched.</summary>
    [Fact]
    public async Task AnIssuedTokenAndASuppressedOne_AreAnsweredIdentically()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.Issued("someone", "the-token"));
        _emailFactory.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EmailChangeEmail("Confirm your new email address", "<html>the body</html>"));
        var forIssued = await UseCase().ExecuteAsync(ARequest(), TestToken);

        GivenTheOutcomeIs(EmailChangeRequestOutcome.Suppressed);
        var forSuppressed = await UseCase().ExecuteAsync(ARequest(), TestToken);

        forSuppressed.IsSuccess.ShouldBe(forIssued.IsSuccess);
        forSuppressed.Error.ShouldBe(forIssued.Error);
    }

    [Fact]
    public async Task AnUnreachableRelay_StillSucceeds()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.Issued("someone", "the-token"));
        _emailFactory.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EmailChangeEmail("Confirm your new email address", "<html>the body</html>"));
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The relay is unreachable."));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ACancelledDelivery_Propagates()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.Issued("someone", "the-token"));
        _emailFactory.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EmailChangeEmail("Confirm your new email address", "<html>the body</html>"));
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => UseCase().ExecuteAsync(ARequest(), TestToken));
    }

    private static RequestEmailChangeCommand ARequest() => new("correct horse battery", "new@example.com");

    private void GivenTheOutcomeIs(EmailChangeRequestOutcome request) =>
        _emailChangeTokens.IssueAsync(_callerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(request);

    private RequestEmailChangeUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(
            _emailChangeTokens,
            _emailFactory,
            _emailSender,
            currentUser,
            new RequestEmailChangeCommandValidator(),
            NullLogger<RequestEmailChangeUseCase>.Instance);

    private RequestEmailChangeUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
