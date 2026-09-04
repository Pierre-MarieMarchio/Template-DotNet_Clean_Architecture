using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Saving;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Saving;

/// <summary>
/// The one place a commit happens is the one place EF's vocabulary stops. Everything above this type
/// speaks of a concurrency conflict; nothing above it should have to name <c>DbUpdateConcurrencyException</c>
/// or reach into its <c>Entries</c>.
/// </summary>
/// <remarks>
/// No database. The context is configured against PostgreSQL so that EF has a provider and a model, and
/// an interceptor throws before a connection is ever opened — the translation under test happens in the
/// <c>catch</c>, not in the driver.
/// </remarks>
public sealed class EfUnitOfWorkTests
{
    [Fact]
    public async Task SaveChangesAsync_TranslatesAConcurrencyConflict_AndKeepsTheOriginal()
    {
        var conflict = new DbUpdateConcurrencyException("zero rows were affected");

        await using var context = AContextThatFailsWith(conflict);

        var failure = await Should.ThrowAsync<ConcurrencyConflictException>(
            async () => await new EfUnitOfWork(context).SaveChangesAsync(TestContext.Current.CancellationToken));

        failure.InnerException.ShouldBeSameAs(
            conflict,
            "the log still has to say which rows lost, and only EF's exception knows.");
    }

    /// <summary>
    /// Only the conflict is translated. A unique-index violation is also a <c>DbUpdateException</c>, and
    /// answering it as 409 <c>concurrency.conflict</c> would tell a client to retry a write that will
    /// never succeed.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_LeavesEveryOtherUpdateFailureAlone()
    {
        var violation = new DbUpdateException("duplicate key value violates a unique constraint");

        await using var context = AContextThatFailsWith(violation);

        var failure = await Should.ThrowAsync<DbUpdateException>(
            async () => await new EfUnitOfWork(context).SaveChangesAsync(TestContext.Current.CancellationToken));

        failure.ShouldBeSameAs(violation);
    }

    private static AppDbContext AContextThatFailsWith(Exception failure)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never-opened;Username=none;Password=none")
            .AddInterceptors(new FailingSaveChangesInterceptor(failure))
            .Options;

        return new AppDbContext(options);
    }
}
