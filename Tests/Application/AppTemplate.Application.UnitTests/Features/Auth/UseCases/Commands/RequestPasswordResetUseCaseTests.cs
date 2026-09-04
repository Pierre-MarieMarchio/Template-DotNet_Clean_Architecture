using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands;

/// <summary>
/// Mirrors <c>ResendConfirmationEmailUseCaseTests</c>: this endpoint answers the same for every
/// address it is given, whatever happens behind that one answer.
/// </summary>
public sealed class RequestPasswordResetUseCaseTests
{
    private readonly IPasswordResetTokens _resetTokens = Substitute.For<IPasswordResetTokens>();
    private readonly IPasswordResetEmailComposer _composer = Substitute.For<IPasswordResetEmailComposer>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly RequestPasswordResetUseCase _useCase;

    public RequestPasswordResetUseCaseTests() =>
        _useCase = new RequestPasswordResetUseCase(
            _resetTokens,
            _composer,
            _emailSender,
            new RequestPasswordResetCommandValidator(),
            NullLogger<RequestPasswordResetUseCase>.Instance);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankEmail_IsRefusedBeforeAnythingIsMinted(string email)
    {
        var result = await _useCase.ExecuteAsync(new RequestPasswordResetCommand(email), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _resetTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AKnownAddress_IsSentAResetLink()
    {
        GivenAnAccountExists();

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();

        await _composer.Received(1).ComposeAsync(
            "someone",
            "someone@example.com",
            "reset-token",
            Arg.Any<CancellationToken>());

        await _emailSender.Received(1).SendAsync(
            "someone@example.com",
            "Reset your password",
            "<html>the body</html>",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownAddress_SendsNothingAndStillSucceeds()
    {
        _resetTokens.IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PendingPasswordReset?)null);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _composer.ReceivedCalls().ShouldBeEmpty();
        _emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>A known address and an unknown one must not be distinguishable from the answer alone.</summary>
    [Fact]
    public async Task AKnownAddressAndAnUnknownOne_AreAnsweredIdentically()
    {
        GivenAnAccountExists();
        var forKnown = await _useCase.ExecuteAsync(ARequest(), TestToken);

        _resetTokens.IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PendingPasswordReset?)null);
        var forUnknown = await _useCase.ExecuteAsync(new RequestPasswordResetCommand("nobody@example.com"), TestToken);

        forUnknown.IsSuccess.ShouldBe(forKnown.IsSuccess);
        forUnknown.Error.ShouldBe(forKnown.Error);
    }

    [Fact]
    public async Task AnUnreachableRelay_StillSucceeds()
    {
        GivenAnAccountExists();

        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The relay is unreachable."));

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ACancelledDelivery_Propagates()
    {
        GivenAnAccountExists();

        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => _useCase.ExecuteAsync(ARequest(), TestToken));
    }

    private static RequestPasswordResetCommand ARequest() => new("someone@example.com");

    private void GivenAnAccountExists()
    {
        _resetTokens.IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PendingPasswordReset("someone", "reset-token"));

        _composer.ComposeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEmail("Reset your password", "<html>the body</html>"));
    }
}
