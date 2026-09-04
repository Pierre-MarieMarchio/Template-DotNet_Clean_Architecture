using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Queries.IssueFileDownload;

/// <summary>
/// Every assertion here is a security assertion. The grant this use case mints is a bearer right —
/// whoever holds the URL reads the file, with no identity attached — so this is the last moment at
/// which "whose file is this?" can be asked at all.
/// </summary>
public sealed class IssueFileDownloadUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private static readonly IssuedDownloadGrant _grant = new(
        "https://store.example/download",
        StubDateTimeProvider.DefaultInstant.AddMinutes(5));

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();

    public IssueFileDownloadUseCaseTests() =>
        _content.CreateDownloadGrantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(_grant);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new IssueFileDownloadQuery(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task AnAnonymousCaller_GetsNoGrant()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new IssueFileDownloadQuery(Guid.CreateVersion7()), TestToken);

        await NoGrantWasMinted();
    }

    /// <summary>
    /// The one that matters most in the whole feature. Deleting the ownership comparison in
    /// <c>StoredFileService</c> hands any authenticated caller a readable URL for any file in the
    /// system, and turns this red.
    /// </summary>
    [Fact]
    public async Task AnotherUsersFile_YieldsNoGrant()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(foreign.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        await NoGrantWasMinted();
    }

    [Fact]
    public async Task AnotherUsersFile_IsIndistinguishableFromAMissingOne()
    {
        var foreign = AStoredFile.AvailableOwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var foreignResult = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(foreign.Id), TestToken);
        var missingResult = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(missingId), TestToken);

        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
    }

    /// <summary>
    /// A pending file's key may hold nothing, or a partial object. A grant for either is a URL that
    /// answers with a broken file and no explanation.
    /// </summary>
    [Fact]
    public async Task AFileWhoseDepositWasNeverConfirmed_YieldsNoGrant()
    {
        var pending = AStoredFile.PendingOwnedBy(_callerId);
        _repository.GetAsync(pending.Id, Arg.Any<CancellationToken>()).Returns(pending);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(pending.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.notAvailable");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await NoGrantWasMinted();
    }

    /// <summary>
    /// A deposited file has all of its bytes, and they match the digest that was declared — it is one
    /// state away from being readable and nothing about it looks broken. It still yields no grant,
    /// because the one thing missing is that anything has read the content, and that is exactly the
    /// gap the whole inspection exists to close. The answer is the same "not yet" a pending file
    /// gets, because the state on the file is where the difference is published.
    /// </summary>
    [Fact]
    public async Task AFileWaitingForAVerdictOnItsContent_YieldsNoGrant()
    {
        var deposited = AStoredFile.DepositedOwnedBy(_callerId);
        _repository.GetAsync(deposited.Id, Arg.Any<CancellationToken>()).Returns(deposited);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(deposited.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("storedFile.notAvailable");
        await NoGrantWasMinted();
    }

    /// <summary>
    /// <b>The test that buys the security of the whole feature.</b> A grant is a bearer right: once
    /// minted, whoever holds the URL reads the object, with no identity attached and nothing left to
    /// check. So this is the last moment at which refused content can be refused at all, and it must
    /// hold however the file got here — which is why the gate names <c>Available</c> rather than
    /// listing the states it will not serve.
    /// </summary>
    [Fact]
    public async Task AQuarantinedFile_CanNeverBeServed()
    {
        var quarantined = AStoredFile.QuarantinedOwnedBy(_callerId);
        _repository.GetAsync(quarantined.Id, Arg.Any<CancellationToken>()).Returns(quarantined);

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(quarantined.Id), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        await NoGrantWasMinted();
    }

    /// <summary>
    /// Its own code, unlike "not yet": a refused file will never become available, and a client that
    /// could not tell the two apart would poll for it until it gave up. The message says the content
    /// was refused and never what was found in it.
    /// </summary>
    [Fact]
    public async Task AQuarantinedFile_IsDistinguishableFromOneStillWaiting()
    {
        var quarantined = AStoredFile.QuarantinedOwnedBy(_callerId);
        var deposited = AStoredFile.DepositedOwnedBy(_callerId);
        _repository.GetAsync(quarantined.Id, Arg.Any<CancellationToken>()).Returns(quarantined);
        _repository.GetAsync(deposited.Id, Arg.Any<CancellationToken>()).Returns(deposited);

        var refused = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(quarantined.Id), TestToken);
        var waiting = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(deposited.Id), TestToken);

        refused.Error!.Code.ShouldBe("storedFile.quarantined");
        refused.Error.Code.ShouldNotBe(waiting.Error!.Code);
        refused.Error.Message.ShouldNotContain("virus", Case.Insensitive);
        refused.Error.Message.ShouldNotContain("svg", Case.Insensitive);
    }

    [Fact]
    public async Task TheCallersOwnAvailableFile_YieldsAGrant()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();

        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(_grant);
    }

    /// <summary>
    /// The grant names the key the aggregate holds, and offers the label the user chose as the
    /// download name — the two are separate values on purpose, and joining them would be the one
    /// change that makes a caller-supplied string decide which bytes are read.
    /// </summary>
    [Fact]
    public async Task TheGrant_NamesTheAggregatesKeyAndOffersTheUsersLabel()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();

        await UseCase().ExecuteAsync(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        await _content.Received(1).CreateDownloadGrantAsync(
            storedFile.ObjectKey.Value,
            storedFile.Name.Value,
            storedFile.DeclaredMediaType.Value,
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The URL is about to land in a browser history, a referrer and every proxy log in between, and
    /// none of those copies can be revoked. Minutes, not hours, is the only thing limiting what they
    /// are still worth.
    /// </summary>
    [Fact]
    public async Task TheDownloadWindow_IsMeasuredInMinutes()
    {
        var storedFile = GivenTheCallerOwnsAnAvailableFile();

        await UseCase().ExecuteAsync(new IssueFileDownloadQuery(storedFile.Id), TestToken);

        await _content.Received(1).CreateDownloadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<TimeSpan>(lifetime => lifetime > TimeSpan.Zero && lifetime <= TimeSpan.FromMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyId_IsAValidationFailure()
    {
        var result = await UseCase().ExecuteAsync(new IssueFileDownloadQuery(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task ANullQuery_IsARejectedArgument() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    private async Task NoGrantWasMinted() =>
        await _content.DidNotReceive().CreateDownloadGrantAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());

    private StoredFile GivenTheCallerOwnsAnAvailableFile()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_callerId);
        _repository.GetAsync(storedFile.Id, Arg.Any<CancellationToken>()).Returns(storedFile);

        return storedFile;
    }

    private IssueFileDownloadUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private IssueFileDownloadUseCase UseCaseFor(ICurrentUser currentUser) => new(
        new StoredFileService(_repository, currentUser),
        _content,
        new IssueFileDownloadQueryValidator());
}
