using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.InMemory.Features.Files;
using AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

public sealed class InMemoryFileContentStoreTests : FileContentStoreContract, IDisposable
{
    private const string _objectKey = "t0/202601/9f2c1d7a4b6e8f0132547698badcfe10";

    private const string _mediaType = "image/png";

    private static readonly byte[] _content = Encoding.UTF8.GetBytes("the deposited bytes");

    private readonly ServiceProvider _provider = FileContentHost.Compose();

    private readonly IServiceScope _scope;

    public InMemoryFileContentStoreTests() => _scope = _provider.CreateScope();

    protected override IFileContentStore Store => FileContentHost.StoreIn(_scope);

    protected override void ShouldLeaveNoTrace() => FileContentHost.BucketOf(_provider).Snapshot().ShouldBeEmpty();

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
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
