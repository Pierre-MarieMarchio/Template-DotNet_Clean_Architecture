using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.Register;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.Register;

public sealed class RegisterUseCaseTests
{
    private const string _email = "someone@example.com";
    private const string _password = "correct horse battery";
    private const string _token = "confirmation-token";

    private static readonly AccountEmail _confirmation = new("Confirm your email address", "<html>confirm</html>");
    private static readonly AccountEmail _notice = new("You already have an account", "<html>notice</html>");

    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly IEmailTokensService _emailTokens = Substitute.For<IEmailTokensService>();
    private readonly IAccountEmailFactory _emailFactory = Substitute.For<IAccountEmailFactory>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly RegisterUseCase _useCase;

    public RegisterUseCaseTests()
    {
        _useCase = new RegisterUseCase(
            _accounts,
            _emailTokens,
            _emailFactory,
            _emailSender,
            _securityEventLog,
            new RegisterCommandValidator(),
            NullLogger<RegisterUseCase>.Instance);

        _emailTokens.IssueEmailConfirmationAsync(_email, Arg.Any<CancellationToken>()).Returns(_token);
        _emailFactory.CreateConfirmationAsync(_email, _token, Arg.Any<CancellationToken>()).Returns(_confirmation);
        _emailFactory.CreateAlreadyRegisteredAsync(_email, Arg.Any<CancellationToken>()).Returns(_notice);
    }

    public enum AddressState
    {
        Unknown,
        TakenAndConfirmed,
        TakenAndUnconfirmed,
    }

    public static TheoryData<AddressState> EveryState => [.. Enum.GetValues<AddressState>()];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUnknownAddress_CreatesTheAccountAndMailsAConfirmationLink()
    {
        var userId = GivenTheAddressIs(AddressState.Unknown);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _securityEventLog.Received(1).Record(SecurityEvent.Registered(userId));
        await _emailSender.Received(1).SendAsync(_email, _confirmation.Subject, _confirmation.HtmlBody, Arg.Any<CancellationToken>());
        await _emailFactory.DidNotReceive().CreateAlreadyRegisteredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATakenConfirmedAddress_MailsANoticeAndNoToken()
    {
        GivenTheAddressIs(AddressState.TakenAndConfirmed);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _emailSender.Received(1).SendAsync(_email, _notice.Subject, _notice.HtmlBody, Arg.Any<CancellationToken>());
        _emailTokens.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ATakenUnconfirmedAddress_MailsAFreshConfirmationLink()
    {
        GivenTheAddressIs(AddressState.TakenAndUnconfirmed);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _emailSender.Received(1).SendAsync(_email, _confirmation.Subject, _confirmation.HtmlBody, Arg.Any<CancellationToken>());
        await _emailFactory.DidNotReceive().CreateAlreadyRegisteredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(EveryState))]
    public async Task EveryState_SendsExactlyOneMail_ToTheSubmittedAddress(AddressState state)
    {
        GivenTheAddressIs(state);

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _emailSender.ReceivedCalls().Count().ShouldBe(1);
        await _emailSender.Received(1).SendAsync(_email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryState_IsAnsweredIdentically()
    {
        var answers = new List<Result>();

        foreach (var state in Enum.GetValues<AddressState>())
        {
            GivenTheAddressIs(state);
            answers.Add(await _useCase.ExecuteAsync(AValidRequest(), TestToken));
        }

        answers.ShouldAllBe(answer => answer.IsSuccess && answer.Error == null);
    }

    [Theory]
    [MemberData(nameof(EveryState))]
    public async Task AFailedDelivery_NeverChangesTheAnswer(AddressState state)
    {
        GivenTheAddressIs(state);
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The relay is unreachable."));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(EveryState))]
    public async Task AFailureWhileComposingTheMessage_NeverChangesTheAnswer(AddressState state)
    {
        GivenTheAddressIs(state);
        _emailFactory.CreateConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The template is missing."));
        _emailFactory.CreateAlreadyRegisteredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The template is missing."));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ACreatedAccount_IsKeptWhenDeliveryFails()
    {
        GivenTheAddressIs(AddressState.Unknown);
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The relay is unreachable."));

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _accounts.ReceivedCalls().Count().ShouldBe(1);
    }

    [Fact]
    public async Task NoIssuedToken_SendsNothing_AndStillSucceeds()
    {
        GivenTheAddressIs(AddressState.TakenAndUnconfirmed);
        _emailTokens.IssueEmailConfirmationAsync(_email, Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(EveryState))]
    public async Task ACancelledDelivery_Propagates(AddressState state)
    {
        GivenTheAddressIs(state);
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => _useCase.ExecuteAsync(AValidRequest(), TestToken));
    }

    [Fact]
    public async Task TheConfirmationToken_IsIssuedAfterTheAccountExists()
    {
        GivenTheAddressIs(AddressState.Unknown);

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        Received.InOrder(() =>
        {
            _accounts.RegisterAsync(_email, _password, Arg.Any<CancellationToken>());
            _emailTokens.IssueEmailConfirmationAsync(_email, Arg.Any<CancellationToken>());
            _emailFactory.CreateConfirmationAsync(_email, _token, Arg.Any<CancellationToken>());
            _emailSender.SendAsync(_email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task TheCancellationToken_ReachesEveryStep()
    {
        GivenTheAddressIs(AddressState.Unknown);
        using var cancellation = new CancellationTokenSource();

        await _useCase.ExecuteAsync(AValidRequest(), cancellation.Token);

        await _accounts.Received(1).RegisterAsync(_email, _password, cancellation.Token);
        await _emailTokens.Received(1).IssueEmailConfirmationAsync(_email, cancellation.Token);
        await _emailSender.Received(1).SendAsync(_email, Arg.Any<string>(), Arg.Any<string>(), cancellation.Token);
    }

    [Theory]
    [InlineData("", _password)]
    [InlineData("not-an-email", _password)]
    [InlineData(_email, "")]
    [InlineData(_email, "   ")]
    public async Task AnInvalidRequest_NeverReachesTheStore(string email, string password)
    {
        var result = await _useCase.ExecuteAsync(new RegisterCommand(email, password), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        _accounts.ReceivedCalls().ShouldBeEmpty();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task APasswordBeyondTheMaximumLength_NeverReachesTheStore()
    {
        var result = await _useCase.ExecuteAsync(
            new RegisterCommand(_email, new string('a', RegisterCommandValidator.MaximumPasswordLength + 1)),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryFailureMessage_ReachesTheCaller()
    {
        var result = await _useCase.ExecuteAsync(new RegisterCommand("", ""), TestToken);

        result.Error!.Details!.Keys.ShouldBe(["email", "password"], ignoreOrder: true);
    }

    [Fact]
    public async Task AStoreRefusal_CarriesEachExplanationOnItsFieldAndSendsNothing()
    {
        _accounts.RegisterAsync(_email, _password, Arg.Any<CancellationToken>())
            .Returns(AccountRegistrationOutcome.Rejected("Email 'x' is invalid.", "Passwords must have at least one digit."));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.Error!.Code.ShouldBe("request.validationFailed");
        result.Error.Details!["email"].ShouldBe(["Email 'x' is invalid."]);
        result.Error.Details["password"].ShouldBe(["Passwords must have at least one digit."]);
        _emailSender.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private static RegisterCommand AValidRequest() => new(_email, _password);

    private Guid GivenTheAddressIs(AddressState state)
    {
        var userId = Guid.CreateVersion7();

        _accounts.RegisterAsync(_email, _password, Arg.Any<CancellationToken>()).Returns(state switch
        {
            AddressState.Unknown => AccountRegistrationOutcome.Created(userId),
            AddressState.TakenAndConfirmed => AccountRegistrationOutcome.AlreadyRegistered(emailConfirmed: true),
            _ => AccountRegistrationOutcome.AlreadyRegistered(emailConfirmed: false),
        });

        return userId;
    }
}
