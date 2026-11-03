using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Services;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.Services;

public sealed class ReminderServiceTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

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

        await _repository.DidNotReceive().GetAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownId_IsReportedAsNotFound()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(_callerId, missingId, Arg.Any<CancellationToken>()).Returns((Reminder?)null);

        var result = await Access().LoadOwnedAsync(missingId, null, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("reminder.notFound");
    }

    [Fact]
    public async Task ANullPrecondition_LeavesTheLoadUnconditional()
    {
        var reminder = AReminder.OwnedBy(_callerId);
        _repository.GetAsync(_callerId, reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        var result = await Access().LoadOwnedAsync(reminder.Id, precondition: null, TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(reminder);
    }

    [Fact]
    public async Task ASatisfiedPrecondition_Succeeds()
    {
        var reminder = AReminder.OwnedBy(_callerId);
        _repository.GetAsync(_callerId, reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);
        var precondition = new VersionPrecondition([reminder.Version]);

        var result = await Access().LoadOwnedAsync(reminder.Id, precondition, TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(reminder);
    }

    [Fact]
    public async Task AnUnsatisfiedPrecondition_Fails()
    {
        var reminder = AReminder.OwnedBy(_callerId);
        _repository.GetAsync(_callerId, reminder.Id, Arg.Any<CancellationToken>()).Returns(reminder);
        var precondition = new VersionPrecondition([reminder.Version + 1]);

        var result = await Access().LoadOwnedAsync(reminder.Id, precondition, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
        result.Error.Code.ShouldBe("precondition.failed");
    }

    private ReminderService AccessFor(ICurrentUser currentUser) => new(_repository, currentUser);

    private ReminderService Access() => AccessFor(StubCurrentUser.WithId(_callerId));
}
