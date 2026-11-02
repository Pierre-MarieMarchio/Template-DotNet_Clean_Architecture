using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Saving;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Saving;

public sealed class EfUnitOfWorkTests
{
    private const string _neverOpened = DatabaseProvider.DesignTimeConnectionString;

    [Fact]
    public async Task SaveChangesAsync_TranslatesAConcurrencyConflict_AndKeepsTheOriginal()
    {
        var conflict = new DbUpdateConcurrencyException("zero rows were affected");

        await using var context = AContextThatFailsWith(conflict);

        var failure = await Should.ThrowAsync<ConcurrencyConflictException>(
            async () => await new EfUnitOfWork<EmptyContext>(context)
                .SaveChangesAsync(TestContext.Current.CancellationToken));

        failure.InnerException.ShouldBeSameAs(
            conflict,
            "the log still has to say which rows lost, and only EF's exception knows.");
    }

    [Fact]
    public async Task SaveChangesAsync_LeavesEveryOtherUpdateFailureAlone()
    {
        var violation = new DbUpdateException("duplicate key value violates a unique constraint");

        await using var context = AContextThatFailsWith(violation);

        var failure = await Should.ThrowAsync<DbUpdateException>(
            async () => await new EfUnitOfWork<EmptyContext>(context)
                .SaveChangesAsync(TestContext.Current.CancellationToken));

        failure.ShouldBeSameAs(violation);
    }

    [Fact]
    public async Task AddContextUnitOfWork_CommitsOnItsOwn_WithNoSavingMechanismRegistered()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EmptyContext>(options => options.UseDatabase(_neverOpened));
        services.AddContextUnitOfWork<EmptyContext>();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        int written = await scope.ServiceProvider
            .GetRequiredService<IContextUnitOfWork<EmptyContext>>()
            .SaveChangesAsync(TestContext.Current.CancellationToken);

        written.ShouldBe(0, "nothing was staged, so nothing was written and no connection was opened");
    }

    private static EmptyContext AContextThatFailsWith(Exception failure)
    {
        var options = new DbContextOptionsBuilder<EmptyContext>()
            .UseDatabase(_neverOpened)
            .AddInterceptors(new FailingSaveChangesInterceptor(failure))
            .Options;

        return new EmptyContext(options);
    }

    private sealed class EmptyContext(DbContextOptions<EmptyContext> options) : DbContext(options);
}
