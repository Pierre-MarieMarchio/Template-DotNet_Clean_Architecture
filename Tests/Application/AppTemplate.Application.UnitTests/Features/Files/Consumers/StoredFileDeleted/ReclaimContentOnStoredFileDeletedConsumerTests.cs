using AppTemplate.Application.Features.Files.Consumers.StoredFileDeleted;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Domain.Features.Files.Events;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Consumers.StoredFileDeleted;

/// <summary>
/// The fast half of reclaiming a deleted file's bytes.
/// </summary>
/// <remarks>
/// Every assertion here is about the consumer being <em>allowed to fail</em>. It exists to shorten
/// the interval between a row disappearing and its bytes following, and the sweep is what makes that
/// happen at all — so a consumer that turned a store outage into a failed deletion, or that stopped
/// the post-commit dispatch for the next consumer in line, would have made the missing outbox a bug
/// rather than a cost.
/// </remarks>
public sealed class ReclaimContentOnStoredFileDeletedConsumerTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private readonly IFileContentStore _content = Substitute.For<IFileContentStore>();

    private ReclaimContentOnStoredFileDeletedConsumer Consumer =>
        new(_content, NullLogger<ReclaimContentOnStoredFileDeletedConsumer>.Instance);

    private static StoredFileDeletedDomainEvent AnEvent(ObjectKey key) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), key, DateTimeOffset.UtcNow);

    [Fact]
    public async Task ItDeletes_TheObjectTheEventNames()
    {
        var key = ObjectKey.New(DateTimeOffset.UtcNow);

        await Consumer.ConsumeAsync(AnEvent(key), TestToken);

        await _content.Received(1).DeleteAsync(key.Value, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A store that is briefly unreachable must not surface as a failed deletion: the row is already
    /// gone and committed, and the sweep reclaims what this call could not.
    /// </summary>
    [Fact]
    public async Task AStoreThatRefuses_DoesNotFailTheDispatch()
    {
        _content
            .DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the store is having a moment"));

        await Should.NotThrowAsync(
            () => Consumer.ConsumeAsync(AnEvent(ObjectKey.New(DateTimeOffset.UtcNow)), TestToken));
    }

    /// <summary>
    /// Shutdown is not a store failure. Swallowing it would report a cancelled run as a completed
    /// reclamation, and would keep the host from stopping promptly.
    /// </summary>
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
