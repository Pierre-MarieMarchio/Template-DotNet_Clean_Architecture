using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.InMemory.Features.Files;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

/// <summary>
/// The double behind <see cref="IFileContentStore"/>, exercised end to end through the container.
/// <para>
/// It holds the bytes it is given and refuses, as a real store does, a body whose length or digest
/// disagrees with the declaration.
/// </para>
/// </summary>
public sealed class InMemoryFileContentStoreTests
{
    private const string _objectKey = "t0/202601/9f2c1d7a4b6e8f0132547698badcfe10";

    private const string _mediaType = "image/png";

    private static readonly byte[] _content = Encoding.UTF8.GetBytes("the deposited bytes");

    /// <summary>
    /// The digest of <see cref="_content"/>, computed rather than written down: a grant binds it, so a
    /// constant that drifted from the bytes would mint a grant no honest deposit could satisfy.
    /// </summary>
    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_content));

    [Fact]
    public async Task WriteAsync_StoresBytesThatMatchTheDeclarationAndReadsThemBack()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        var store = FileContentHost.StoreIn(scope);

        var outcome = await store.WriteAsync(
            _objectKey,
            new MemoryStream(_content),
            _mediaType,
            _content.Length,
            _checksum,
            TestContext.Current.CancellationToken);

        outcome.ShouldBe(ContentWriteOutcome.Written);
        await using var read = await store.OpenReadAsync(_objectKey, TestContext.Current.CancellationToken);
        read.ShouldNotBeNull();
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy, TestContext.Current.CancellationToken);
        copy.ToArray().ShouldBe(_content);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public async Task WriteAsync_RefusesBytesThatDisagreeWithTheDeclarationAndStoresNothing(
        int sizeOffset,
        bool wrongDigest)
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();

        var outcome = await FileContentHost.StoreIn(scope).WriteAsync(
            _objectKey,
            new MemoryStream(_content),
            _mediaType,
            _content.Length + sizeOffset,
            wrongDigest ? new string('0', 64) : _checksum,
            TestContext.Current.CancellationToken);

        outcome.ShouldBe(ContentWriteOutcome.ContentMismatch);
        FileContentHost.BucketOf(provider).Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenReadAsync_AnswersNullForAKeyThatHoldsNothing()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();

        (await FileContentHost.StoreIn(scope).OpenReadAsync(_objectKey, TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }

    [Fact]
    public async Task DescribeAsync_ReportsWhatTheDepositMeasuredAndDigested()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        FileContentHost.BucketOf(provider).Deposit(_objectKey, _mediaType, _content);

        var description = await FileContentHost.StoreIn(scope)
            .DescribeAsync(_objectKey, TestContext.Current.CancellationToken);

        description.ShouldBe(new StoredObjectDescription(
            _content.Length,
            Convert.ToHexStringLower(SHA256.HashData(_content))));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheObjectAndSaysNothingTheSecondTime()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        var store = FileContentHost.StoreIn(scope);
        FileContentHost.BucketOf(provider).Deposit(_objectKey, _mediaType, _content);

        await store.DeleteAsync(_objectKey, TestContext.Current.CancellationToken);
        await Should.NotThrowAsync(store.DeleteAsync(_objectKey, TestContext.Current.CancellationToken));

        FileContentHost.BucketOf(provider).Snapshot().ShouldBeEmpty();
    }

    /// <summary>
    /// One bucket for the whole host, so an object deposited in one request is found in the next —
    /// which is what every assertion made after a sequence of requests depends on.
    /// </summary>
    [Fact]
    public async Task TheDoubles_ShareOneBucketAcrossEveryScope()
    {
        using var provider = FileContentHost.Compose();

        using (var writing = provider.CreateScope())
        {
            FileContentHost.BucketOf(writing.ServiceProvider).Deposit(_objectKey, _mediaType, _content);
        }

        using var reading = provider.CreateScope();

        (await FileContentHost.StoreIn(reading).DescribeAsync(_objectKey, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
    }

    /// <summary>
    /// Replacement, not addition: the module removes what a real module registered before adding its
    /// own doubles. A double that won by being registered last is a silent dependency on composition
    /// order whose failure mode is a test quietly talking to a real bucket.
    /// </summary>
    [Fact]
    public void AddInMemoryModule_ReplacesFilePortsThatWereAlreadyRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IFileContentStore>());
        services.AddSingleton(Substitute.For<IFileContentStore>());

        services.AddInMemoryModule();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IFileContentStore>().ShouldHaveSingleItem();
    }

    [Fact]
    public void AddInMemoryFileContent_ReplacesTheFilePortsWithoutTouchingTheClock()
    {
        var real = Substitute.For<IDateTimeProvider>();
        var services = new ServiceCollection();
        services.AddSingleton(real);

        services.AddInMemoryFileContent();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        provider.GetRequiredService<IDateTimeProvider>().ShouldBeSameAs(real);
        provider.GetRequiredService<StoredObjects>().ShouldNotBeNull();
    }
}
