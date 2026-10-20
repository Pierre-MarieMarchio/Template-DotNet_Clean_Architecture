using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestEmailChange;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.RequestEmailChange;

public sealed class RequestEmailChangeUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IEmailTokensService _emailTokens = Substitute.For<IEmailTokensService>();
    private readonly IAccountEmailFactory _emailFactory = Substitute.For<IAccountEmailFactory>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RequestEmailChangeCommand("correct horse battery", "new@example.com"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _emailTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "new@example.com")]
    [InlineData("old password", "not-an-email")]
    public async Task AMalformedRequest_NeverReachesTheStore(string currentPassword, string newEmail)
    {
        var result = await UseCase().ExecuteAsync(new RequestEmailChangeCommand(currentPassword, newEmail), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _emailTokens.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>A token that outlived its account — see <c>GetCurrentUserUseCase</c>.</summary>
    [Fact]
    public async Task AnAccountThatNoLongerExists_IsAnsweredAsUnauthenticatedAndSendsNothing()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _emailSender.ReceivedCalls().ShouldBeEmpty();
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
        GivenTheOutcomeIs(EmailChangeRequestOutcome.Issued("the-token"));
        _emailFactory.CreateEmailChangeAsync("new@example.com", "the-token", Arg.Any<CancellationToken>())
            .Returns(new AccountEmail("Confirm your new email address", "<html>the body</html>"));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();

        await _emailSender.Received(1).SendAsync(
            "new@example.com",
            "Confirm your new email address",
            "<html>the body</html>",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAlreadyTakenAddress_IsSentTheNotice_AndStillSucceeds()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.AddressAlreadyRegistered);
        _emailFactory.CreateAlreadyRegisteredAsync("new@example.com", Arg.Any<CancellationToken>())
            .Returns(new AccountEmail("You already have an account", "<html>notice</html>"));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _emailSender.Received(1).SendAsync(
            "new@example.com",
            "You already have an account",
            "<html>notice</html>",
            Arg.Any<CancellationToken>());
        await _emailFactory.DidNotReceive().CreateEmailChangeAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFreeAddressAndATakenOne_AreAnsweredIdentically()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.Issued("the-token"));
        _emailFactory.CreateEmailChangeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountEmail("Confirm your new email address", "<html>the body</html>"));
        var forFree = await UseCase().ExecuteAsync(ARequest(), TestToken);

        GivenTheOutcomeIs(EmailChangeRequestOutcome.AddressAlreadyRegistered);
        _emailFactory.CreateAlreadyRegisteredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountEmail("You already have an account", "<html>notice</html>"));
        var forTaken = await UseCase().ExecuteAsync(ARequest(), TestToken);

        forTaken.IsSuccess.ShouldBe(forFree.IsSuccess);
        forTaken.Error.ShouldBe(forFree.Error);
        _emailSender.ReceivedCalls().Count().ShouldBe(2);
    }

    [Fact]
    public async Task AFailedNotice_StillSucceeds()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.AddressAlreadyRegistered);
        _emailFactory.CreateAlreadyRegisteredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountEmail("You already have an account", "<html>notice</html>"));
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The relay is unreachable."));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AnUnreachableRelay_StillSucceeds()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.Issued("the-token"));
        _emailFactory.CreateEmailChangeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountEmail("Confirm your new email address", "<html>the body</html>"));
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The relay is unreachable."));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ACancelledDelivery_Propagates()
    {
        GivenTheOutcomeIs(EmailChangeRequestOutcome.Issued("the-token"));
        _emailFactory.CreateEmailChangeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AccountEmail("Confirm your new email address", "<html>the body</html>"));
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => UseCase().ExecuteAsync(ARequest(), TestToken));
    }

    private static RequestEmailChangeCommand ARequest() => new("correct horse battery", "new@example.com");

    private void GivenTheOutcomeIs(EmailChangeRequestOutcome request) =>
        _emailTokens.IssueEmailChangeAsync(_callerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(request);

    private RequestEmailChangeUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(
            _emailTokens,
            _emailFactory,
            _emailSender,
            currentUser,
            new RequestEmailChangeCommandValidator(),
            NullLogger<RequestEmailChangeUseCase>.Instance);

    private RequestEmailChangeUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
