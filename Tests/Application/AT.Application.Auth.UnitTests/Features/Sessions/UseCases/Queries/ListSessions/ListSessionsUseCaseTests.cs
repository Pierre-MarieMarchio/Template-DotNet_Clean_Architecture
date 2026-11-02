using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.ListSessions;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Sessions.UseCases.Queries.ListSessions;

public sealed class ListSessionsUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ISessionsService _sessions = Substitute.For<ISessionsService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous, StubCurrentSession.WithoutSession)
            .ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _sessions.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCallersSessions_AreListedAsThePortReturnsThem()
    {
        var listed = new[]
        {
            new ActiveSession(Guid.CreateVersion7(), _monday, _monday, "a-client/1.0"),
            new ActiveSession(Guid.CreateVersion7(), _tuesday, _wednesday, null),
        };

        _sessions.ListActiveAsync(_callerId, Arg.Any<CancellationToken>()).Returns(listed);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value[0].Id.ShouldBe(listed[0].Id);
        result.Value[0].CreatedAt.ShouldBe(listed[0].CreatedAt);
        result.Value[0].LastUsedAt.ShouldBe(listed[0].LastUsedAt);
        result.Value[0].UserAgent.ShouldBe(listed[0].UserAgent);
    }

    [Fact]
    public async Task TheSessionTheRequestIsMadeFrom_IsMarkedCurrent()
    {
        var current = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();

        _sessions.ListActiveAsync(_callerId, Arg.Any<CancellationToken>()).Returns(
        [
            new ActiveSession(current, _monday, _monday, null),
            new ActiveSession(other, _tuesday, _wednesday, null),
        ]);

        var result = await UseCaseFor(StubCurrentUser.WithId(_callerId), StubCurrentSession.InSession(current))
            .ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single(session => session.Current).Id.ShouldBe(current);
    }

    [Fact]
    public async Task ATokenWithoutASession_MarksNothingCurrent()
    {
        _sessions.ListActiveAsync(_callerId, Arg.Any<CancellationToken>()).Returns(
        [
            new ActiveSession(Guid.CreateVersion7(), _monday, _monday, null),
        ]);

        var result = await UseCaseFor(StubCurrentUser.WithId(_callerId), StubCurrentSession.WithoutSession)
            .ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldAllBe(session => !session.Current);
    }

    private static readonly DateTimeOffset _monday = DateTimeOffset.UnixEpoch.AddDays(1);

    private static readonly DateTimeOffset _tuesday = DateTimeOffset.UnixEpoch.AddDays(2);

    private static readonly DateTimeOffset _wednesday = DateTimeOffset.UnixEpoch.AddDays(3);

    private ListSessionsUseCase UseCase() =>
        UseCaseFor(StubCurrentUser.WithId(_callerId), StubCurrentSession.WithoutSession);

    private ListSessionsUseCase UseCaseFor(ICurrentUser currentUser, StubCurrentSession currentSession) =>
        new(_sessions, currentUser, currentSession);
}
