using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Services.Consumers.StoredFileDeleted;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Services.Consumers.StoredFileDeleted;

public sealed class ReclaimContentOnStoredFileDeletedConsumerTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();

    private ReclaimContentOnStoredFileDeletedConsumer Consumer =>
        new(_content, NullLogger<ReclaimContentOnStoredFileDeletedConsumer>.Instance);

    private static StoredFileDeletedDomainEvent AnEvent(ObjectKey key) =>
        new(Guid.CreateVersion7(), UserId.Create(Guid.CreateVersion7()), key, DateTimeOffset.UtcNow);

    [Fact]
    public async Task ItDeletes_TheObjectTheEventNames()
    {
        var key = ObjectKey.New(DateTimeOffset.UtcNow);

        await Consumer.ConsumeAsync(AnEvent(key), TestToken);

        await _content.Received(1).DeleteAsync(key.Value, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreThatRefuses_DoesNotFailTheDispatch()
    {
        _content
            .DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the store is having a moment"));

        await Should.NotThrowAsync(
            () => Consumer.ConsumeAsync(AnEvent(ObjectKey.New(DateTimeOffset.UtcNow)), TestToken));
    }

    [Fact]
    public async Task ACancelledRun_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        _content
            .DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(
            () => Consumer.ConsumeAsync(AnEvent(ObjectKey.New(DateTimeOffset.UtcNow)), cancelled.Token));
    }
}
