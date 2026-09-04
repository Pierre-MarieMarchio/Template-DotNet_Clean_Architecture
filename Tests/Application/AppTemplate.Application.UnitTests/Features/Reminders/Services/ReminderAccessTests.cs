using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Reminders.Errors;
using AppTemplate.Application.Features.Reminders.Services;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.Services;

/// <summary>
/// The one gate every reminder command loads its aggregate through, so its own tests are where
/// the identity/ownership/precondition matrix is proven exhaustively — same rationale as
/// <c>TodoListAccessTests</c>.
/// </summary>
public sealed class ReminderAccessTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IReminderRepository _repository = Substitute.For<IReminderRepository>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await AccessFor(StubCurrentUser.Anonymous)
            .LoadOwnedAsync(Guid.CreateVersion7(), precondition: null, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_NeverReachesTheRepository()
    {
        await AccessFor(StubCurrentUser.Anonymous).LoadOwnedAsync(Guid.CreateVersion7(), null, TestToken);

        await _repository.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownId_IsReportedAsNotFound()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((Reminder?)null);

        var result = await Access().LoadOwnedAsync(missingId, null, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("reminder.notFound");
    }

    /// <summary>
    /// "Not yours" and "does not exist" answer identically, so a caller cannot use this to
    /// enumerate other users' reminder ids.
    /// </summary>
    [Fact]
    public async Task AnotherUsersReminder_IsIndistinguishableFromAMissingOne()
    {
        var foreign = AReminder.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((Reminder?)null);

        var foreignResult = await Access().LoadOwnedAsync(foreign.Id, null, TestToken);
        var missingResult = await Access().LoadOwnedAsync(missingId, null, TestToken);

        foreignResult.IsFailure.ShouldBeTrue();
        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
    }

    [Fact]
    public async Task ANullPrecondition_LeavesTheLoadUnconditional()
    {
        var reminder = AReminder.OwnedBy(_callerId);
        _repository.GetAsync(reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        var result = await Access().LoadOwnedAsync(reminder.Id, precondition: null, TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(reminder);
    }

    [Fact]
    public async Task ASatisfiedPrecondition_Succeeds()
    {
        var reminder = AReminder.OwnedBy(_callerId);
        _repository.GetAsync(reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);
        var precondition = new VersionPrecondition([reminder.Version]);

        var result = await Access().LoadOwnedAsync(reminder.Id, precondition, TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(reminder);
    }

    [Fact]
    public async Task AnUnsatisfiedPrecondition_Fails()
    {
        var reminder = AReminder.OwnedBy(_callerId);
        _repository.GetAsync(reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);
        var precondition = new VersionPrecondition([reminder.Version + 1]);

        var result = await Access().LoadOwnedAsync(reminder.Id, precondition, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
        result.Error.Code.ShouldBe("precondition.failed");
    }

    [Fact]
    public async Task APreconditionOnAForeignReminder_IsReportedAsNotFoundNotAsPreconditionFailed()
    {
        var foreign = AReminder.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var precondition = new VersionPrecondition([foreign.Version]);

        var result = await Access().LoadOwnedAsync(foreign.Id, precondition, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(ReminderErrors.ReminderNotFound(foreign.Id).Code);
    }

    private ReminderAccess AccessFor(ICurrentUser currentUser) => new(_repository, currentUser);

    private ReminderAccess Access() => AccessFor(StubCurrentUser.WithId(_callerId));
}
