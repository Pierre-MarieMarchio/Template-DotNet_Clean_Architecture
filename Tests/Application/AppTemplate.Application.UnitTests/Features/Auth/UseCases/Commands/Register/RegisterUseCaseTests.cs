using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.ConfirmationEmailComposer;
using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Register;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.Register;

/// <summary>
/// Sign-up is three steps in a fixed order — create the account, mint a confirmation token, deliver
/// the mail — and only the first of them may fail the request.
/// </summary>
public sealed class RegisterUseCaseTests
{
    private const string _token = "confirmation-token";

    private readonly IUserAccounts _accounts = Substitute.For<IUserAccounts>();
    private readonly IEmailConfirmationTokens _confirmationTokens = Substitute.For<IEmailConfirmationTokens>();
    private readonly IConfirmationEmailComposer _composer = Substitute.For<IConfirmationEmailComposer>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly RegisterUseCase _useCase;

    public RegisterUseCaseTests() =>
        _useCase = new RegisterUseCase(
            _accounts,
            _confirmationTokens,
            _composer,
            _emailSender,
            _securityEventLog,
            new RegisterCommandValidator(),
            NullLogger<RegisterUseCase>.Instance);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region The dead-password-policy regression

    /// <summary>
    /// Guards the whole password policy against becoming dead code: removing the
    /// <c>if (!validation.IsValid)</c> check turns every case here red, because the account would be
    /// created anyway.
    /// </summary>
    [Theory]
    [InlineData("", "someone@example.com", "correct horse battery")]
    [InlineData("   ", "someone@example.com", "correct horse battery")]
    [InlineData("someone", "", "correct horse battery")]
    [InlineData("someone", "not-an-email", "correct horse battery")]
    [InlineData("someone", "someone@example.com", "")]
    [InlineData("someone", "someone@example.com", "short")]
    public async Task AnInvalidRequest_NeverCreatesAnAccount(string userName, string email, string password)
    {
        var result = await _useCase.ExecuteAsync(new RegisterCommand(userName, email, password), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// The absolute password floor mirrors the hard minimum the Identity configuration
    /// cannot go below. One character short must be refused, exactly the minimum accepted.
    /// </summary>
    [Fact]
    public async Task APasswordOneCharacterBelowTheFloor_IsRefused()
    {
        string tooShort = new('a', RegisterCommandValidator.AbsoluteMinimumPasswordLength - 1);

        var result = await _useCase.ExecuteAsync(
            new RegisterCommand("someone", "someone@example.com", tooShort),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details!["password"].Any(message => message.Contains("at least", StringComparison.Ordinal))
            .ShouldBeTrue();
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task APasswordExactlyAtTheFloor_IsAccepted()
    {
        string atTheFloor = new('a', RegisterCommandValidator.AbsoluteMinimumPasswordLength);
        GivenTheAccountIsCreated();
        GivenTheConfirmationTokenIsIssued();

        var result = await _useCase.ExecuteAsync(
            new RegisterCommand("someone", "someone@example.com", atTheFloor),
            TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// The upper bound exists to stop an arbitrarily long PBKDF2 input from becoming a
    /// denial of service, so it must be enforced before the hashing store is reached.
    /// </summary>
    [Fact]
    public async Task APasswordBeyondTheMaximumLength_NeverReachesTheStore()
    {
        string tooLong = new('a', RegisterCommandValidator.MaximumPasswordLength + 1);

        var result = await _useCase.ExecuteAsync(
            new RegisterCommand("someone", "someone@example.com", tooLong),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUserNameBeyondTheMaximumLength_IsRefused()
    {
        string tooLong = new('a', RegisterCommandValidator.MaximumUserNameLength + 1);

        var result = await _useCase.ExecuteAsync(
            new RegisterCommand(tooLong, "someone@example.com", "correct horse battery"),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryFailureMessage_ReachesTheCaller()
    {
        var result = await _useCase.ExecuteAsync(new RegisterCommand("", "", ""), TestToken);

        result.Error!.Details.ShouldNotBeNull();
        result.Error.Details.ShouldContainKey("userName");
        result.Error.Details.ShouldContainKey("email");
        result.Error.Details.ShouldContainKey("password");
    }

    #endregion

    #region What the store's refusal becomes

    /// <summary>
    /// A taken address cannot be hidden entirely, but the message must not say which of the two
    /// fields collided, and it must not send mail to an address somebody else owns.
    /// </summary>
    [Fact]
    public async Task ATakenAddress_IsAConflictAndSendsNothing()
    {
        _accounts.CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(AccountCreation.Conflict);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("auth.register.unavailable");
        _confirmationTokens.ReceivedCalls().ShouldBeEmpty();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// A rejection describes the submitted values, so its text is returned verbatim, attached to the
    /// field it concerns — that is the only way a caller learns which rule the password broke, and
    /// which field to point at.
    /// </summary>
    [Fact]
    public async Task ARejection_CarriesTheStoresOwnExplanationOnThePasswordField()
    {
        _accounts.CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(AccountCreation.Rejected("Passwords must have at least one digit."));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details!["password"].ShouldContain("Passwords must have at least one digit.");
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    #endregion

    #region Commit before delivery

    [Fact]
    public async Task ACreatedAccount_IsReportedWithItsIdAndAConfirmationEmail()
    {
        var userId = GivenTheAccountIsCreated();
        GivenTheConfirmationTokenIsIssued();

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(userId);
        result.Value.UserName.ShouldBe("someone");
        result.Value.Email.ShouldBe("someone@example.com");
        result.Value.ConfirmationEmailSent.ShouldBeTrue();
    }

    [Fact]
    public async Task ACreatedAccount_IsRecordedAsARegistration()
    {
        var userId = GivenTheAccountIsCreated();
        GivenTheConfirmationTokenIsIssued();

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.Registered(userId));
    }

    /// <summary>A conflict or a rejection is not a registration: nothing is created, so nothing is logged.</summary>
    [Fact]
    public async Task ATakenAddress_IsNotRecordedAsARegistration()
    {
        _accounts.CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(AccountCreation.Conflict);

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// The account is committed before delivery is attempted. Failing the call would leave an
    /// unconfirmable account behind with its address taken and no way to ask for another link, so the
    /// outage is reported as a flag and nothing undoes the account.
    /// </summary>
    [Fact]
    public async Task AnUnreachableRelay_StillSucceeds_AndLeavesTheAccountAlone()
    {
        var userId = GivenTheAccountIsCreated();
        GivenTheConfirmationTokenIsIssued();

        _emailSender.SendAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The relay is unreachable."));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue("the account was created; only delivery failed.");
        result.Value.UserId.ShouldBe(userId);
        result.Value.ConfirmationEmailSent.ShouldBeFalse();

        // Creating it is the only thing that happened to the account: there is no compensating
        // delete, which is what makes the resend endpoint a working recovery path.
        await _accounts.Received(1).CreateAsync(
            "someone",
            "someone@example.com",
            "correct horse battery",
            Arg.Any<CancellationToken>());

        _accounts.ReceivedCalls().Count().ShouldBe(1);
    }

    /// <summary>A failure while rendering the message is the same kind of failure as an outage.</summary>
    [Fact]
    public async Task AFailureWhileComposingTheMessage_StillSucceeds()
    {
        GivenTheAccountIsCreated();
        GivenTheConfirmationTokenIsIssued();

        _composer.ComposeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The template is missing."));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ConfirmationEmailSent.ShouldBeFalse();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// Cancellation is not an outage: it must not be swallowed into
    /// <c>confirmationEmailSent: false</c>, which would report a completed sign-up for a request
    /// nobody is listening to any more.
    /// </summary>
    [Fact]
    public async Task ACancelledDelivery_Propagates()
    {
        GivenTheAccountIsCreated();
        GivenTheConfirmationTokenIsIssued();

        _emailSender.SendAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(
            () => _useCase.ExecuteAsync(AValidRequest(), TestToken));
    }

    #endregion

    #region Ordering

    /// <summary>
    /// The token is derived from the stored account, so one minted before the row existed would not
    /// confirm it. Swapping the two steps turns this red.
    /// </summary>
    [Fact]
    public async Task TheConfirmationToken_IsIssuedAfterTheAccountExists()
    {
        GivenTheAccountIsCreated();
        GivenTheConfirmationTokenIsIssued();

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        Received.InOrder(() =>
        {
            _accounts.CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());

            _confirmationTokens.IssueAsync("someone@example.com", Arg.Any<CancellationToken>());

            _composer.ComposeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());

            _emailSender.SendAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    /// The mail has to carry the token that was just minted, and be addressed to the account's own
    /// name — a link built from anything else confirms nothing.
    /// </summary>
    [Fact]
    public async Task TheMail_CarriesTheIssuedTokenAndGoesToTheRegisteredAddress()
    {
        GivenTheAccountIsCreated();
        GivenTheConfirmationTokenIsIssued();

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        await _composer.Received(1).ComposeAsync(
            "someone",
            "someone@example.com",
            _token,
            Arg.Any<CancellationToken>());

        await _emailSender.Received(1).SendAsync(
            "someone@example.com",
            "Confirm your email address",
            "<html>the body</html>",
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// No token means no account is waiting for one, which cannot happen straight after a successful
    /// creation — so nothing is sent rather than a mail with an empty link.
    /// </summary>
    [Fact]
    public async Task NoIssuedToken_SendsNothing_AndReportsDeliveryDidNotHappen()
    {
        GivenTheAccountIsCreated();

        _confirmationTokens.IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PendingConfirmation?)null);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ConfirmationEmailSent.ShouldBeFalse();
        _composer.ReceivedCalls().ShouldBeEmpty();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCancellationToken_ReachesEveryStep()
    {
        GivenTheAccountIsCreated();
        GivenTheConfirmationTokenIsIssued();
        using var cancellation = new CancellationTokenSource();

        await _useCase.ExecuteAsync(AValidRequest(), cancellation.Token);

        await _accounts.Received(1).CreateAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            cancellation.Token);

        await _confirmationTokens.Received(1).IssueAsync(Arg.Any<string>(), cancellation.Token);

        await _emailSender.Received(1).SendAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            cancellation.Token);
    }

    #endregion

    private static RegisterCommand AValidRequest() =>
        new("someone", "someone@example.com", "correct horse battery");

    private Guid GivenTheAccountIsCreated()
    {
        var userId = Guid.CreateVersion7();

        _accounts.CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(AccountCreation.Created(userId));

        return userId;
    }

    private void GivenTheConfirmationTokenIsIssued()
    {
        _confirmationTokens.IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PendingConfirmation("someone", _token));

        _composer.ComposeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new ConfirmationEmail("Confirm your email address", "<html>the body</html>"));
    }
}
