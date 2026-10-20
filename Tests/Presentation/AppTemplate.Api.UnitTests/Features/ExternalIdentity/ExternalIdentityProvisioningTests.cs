using AppTemplate.Api.Common.Security;
using AppTemplate.Api.UnitTests.Composition;
using AppTemplate.Application;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityStore;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.ExternalIdentity;

public sealed class ExternalIdentityProvisioningTests : IAsyncLifetime
{
    private const string _issuer = "https://idp.example.com/realms/app";

    private readonly SettableClock _clock = new();
    private readonly RecordingPublisher _published = new();
    private string _database = null!;
    private SqliteConnection _keeper = null!;
    private ServiceProvider _services = null!;

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = SqliteSavingHost.AnInMemoryDatabase("external-identity");
        _keeper = new SqliteConnection(_database);
        await _keeper.OpenAsync(TestToken);

        _services = Compose(decorate: null);
        await SqliteSavingHost.CreateSchemaAsync<AppDbContext>(_services, _keeper, TestToken);
    }

    private ServiceProvider Compose(Func<IExternalIdentityStore, IExternalIdentityStore>? decorate)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = "Host=nowhere;Database=apptemplate;Username=u;Password=p",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        services.AddApiCallerIdentity();
        services.AddExternalIdentity();
        services.AddExternalIdentityPersistence(configuration);
        services.AddSingleton<IDateTimeProvider>(_clock);
        services.AddSingleton<IDomainEventPublisher>(_published);
        SqliteSavingHost.OnSqlite<AppDbContext>(services, _database);

        if (decorate is not null)
        {
            var real = services.Single(descriptor => descriptor.ServiceType == typeof(IExternalIdentityStore));
            services.Remove(real);
            services.AddScoped(provider => decorate(
                (IExternalIdentityStore)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!)));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _keeper.DisposeAsync();
    }

    [Fact]
    public async Task AFirstRequest_CreatesOneMapping_ToANewOwner()
    {
        var owner = await ResolveAsync(Caller("subject-1", "a@example.com", "Ada"));

        var rows = await RowsAsync();
        rows.Count.ShouldBe(1);
        rows[0].UserId.ShouldBe(owner.Value);
        rows[0].Email.ShouldBe("a@example.com");
        rows[0].DisplayName.ShouldBe("Ada");
    }

    [Fact]
    public async Task TheSameCallerAgain_IsTheSameOwner_AndNoSecondRow()
    {
        var first = await ResolveAsync(Caller("subject-1"));
        var second = await ResolveAsync(Caller("subject-1"));

        second.ShouldBe(first);
        (await RowsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task TwoSubjects_AreTwoOwners()
    {
        var first = await ResolveAsync(Caller("subject-1", "same@example.com"));
        var second = await ResolveAsync(Caller("subject-2", "same@example.com"));

        second.ShouldNotBe(first);
    }

    [Fact]
    public async Task TheSameSubjectFromAnotherIssuer_IsAnotherOwner()
    {
        var first = await ResolveAsync(Caller("subject-1"));
        var second = await ResolveAsync(Caller("subject-1") with { Issuer = "https://other.example.com" });

        second.ShouldNotBe(first);
    }

    [Fact]
    public async Task AChangedProfile_IsRefreshed_AndTheOwnerKept()
    {
        var owner = await ResolveAsync(Caller("subject-1", "old@example.com", "Old"));

        var again = await ResolveAsync(Caller("subject-1", "new@example.com", "New"));

        again.ShouldBe(owner);
        var row = (await RowsAsync()).Single();
        row.Email.ShouldBe("new@example.com");
        row.DisplayName.ShouldBe("New");
        row.UserId.ShouldBe(owner.Value);
    }

    [Fact]
    public async Task AnUnchangedProfile_SeenAgainWithinTheGranularity_WritesNothing()
    {
        await ResolveAsync(Caller("subject-1", "a@example.com"));
        var firstSeen = (await RowsAsync()).Single().LastSeenAt;

        _clock.Advance(TimeSpan.FromMinutes(10));
        await ResolveAsync(Caller("subject-1", "a@example.com"));

        (await RowsAsync()).Single().LastSeenAt.ShouldBe(firstSeen);
    }

    [Fact]
    public async Task AnUnchangedProfile_SeenAfterTheGranularity_MovesLastSeen()
    {
        await ResolveAsync(Caller("subject-1", "a@example.com"));

        _clock.Advance(TimeSpan.FromHours(2));
        await ResolveAsync(Caller("subject-1", "a@example.com"));

        (await RowsAsync()).Single().LastSeenAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task AnOversizedAddress_IsNotKept_AndAnOversizedNameIsShortened()
    {
        await ResolveAsync(Caller("subject-1", new string('a', 400) + "@example.com", new string('n', 300)));

        var row = (await RowsAsync()).Single();
        row.Email.ShouldBeNull();
        row.DisplayName!.Length.ShouldBe(256);
    }

    [Fact]
    public async Task AnInsertForAnExistingPair_IsRefusedByTheIndex_AndReportedAsLost()
    {
        var link = new ExternalIdentityLink(_issuer, "subject-1", UserId.Create(Guid.CreateVersion7()), null, null, _clock.UtcNow);
        var rival = link with { UserId = UserId.Create(Guid.CreateVersion7()) };

        await using var scope = _services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>();

        (await store.TryAddAsync(link, TestToken)).ShouldBeTrue();
        (await store.TryAddAsync(rival, TestToken)).ShouldBeFalse();

        (await RowsAsync()).Single().UserId.ShouldBe(link.UserId.Value);
    }

    [Fact]
    public async Task ALostFirstRequestRace_AnswersTheWinnersOwner()
    {
        var winner = UserId.Create(Guid.CreateVersion7());
        var rival = new ExternalIdentityLink(_issuer, "subject-1", winner, null, null, _clock.UtcNow);

        await using var racing = Compose(real => new RivalInsertsFirst(real, rival));
        await using var scope = racing.CreateAsyncScope();

        var owner = await scope.ServiceProvider.GetRequiredService<IExternalIdentityService>()
            .ResolveAsync(Caller("subject-1"), TestToken);

        owner.ShouldBe(winner);
        (await RowsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentFirstRequests_AllAnswerOneOwner()
    {
        var owners = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                return await ResolveAsync(Caller("subject-1"));
            }
            catch (DbUpdateException)
            {
                return null;
            }
        }));

        owners.Where(owner => owner is not null).Distinct().Count().ShouldBe(1);
        (await RowsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task LocalRoles_AreReadForTheOwner_InOrdinalOrder()
    {
        var owner = await ResolveAsync(Caller("subject-1"));
        await ExecuteAsync(
            $"INSERT INTO \"RoleAssignments\" (\"UserId\", \"Role\") VALUES ('{owner.Value.ToString().ToUpperInvariant()}', 'Reader'), ('{owner.Value.ToString().ToUpperInvariant()}', 'Admin')");

        await using var scope = _services.CreateAsyncScope();
        var roles = await scope.ServiceProvider.GetRequiredService<IExternalIdentityService>()
            .ListAssignedRolesAsync(owner, TestToken);

        roles.ShouldBe(["Admin", "Reader"]);
    }

    [Fact]
    public async Task TheProfile_IsTheMostRecentlySeenMappingOfTheOwner()
    {
        var owner = await ResolveAsync(Caller("old-subject", "old@example.com", "Old"));

        _clock.Advance(TimeSpan.FromDays(1));
        await using (var scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>().TryAddAsync(
                new ExternalIdentityLink("https://new.example.com", "new-subject", owner, "new@example.com", "New", _clock.UtcNow),
                TestToken);
        }

        await using var read = _services.CreateAsyncScope();
        var profile = await read.ServiceProvider.GetRequiredService<IExternalIdentityService>()
            .FindProfileAsync(owner, TestToken);

        profile.ShouldNotBeNull();
        profile.Email.ShouldBe("new@example.com");
        (await ResolveAsync(new ExternalCallerDto("https://new.example.com", "new-subject", null, null))).ShouldBe(owner);
    }

    [Fact]
    public async Task ADeletedOwner_LosesItsMappingsAndRoles_AndIsPublished()
    {
        var owner = await ResolveAsync(Caller("subject-1", "a@example.com", "Ada"));
        await ExecuteAsync(
            $"INSERT INTO \"RoleAssignments\" (\"UserId\", \"Role\") VALUES ('{owner.Value.ToString().ToUpperInvariant()}', 'Admin')");

        await using var scope = _services.CreateAsyncScope();
        var deletion = await scope.ServiceProvider.GetRequiredService<IExternalIdentityService>()
            .DeleteAsync(owner, TestToken);

        deletion.IsSuccess.ShouldBeTrue();
        (await RowsAsync()).ShouldBeEmpty();

        await using var read = _services.CreateAsyncScope();
        (await read.ServiceProvider.GetRequiredService<IExternalIdentityService>()
            .ListAssignedRolesAsync(owner, TestToken)).ShouldBeEmpty();

        var fact = _published.Events.OfType<UserAccountDeleted>().Single();
        fact.UserId.ShouldBe(owner);
        fact.OccurredAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task ASecondDeletionOfTheSameOwner_IsNotFound()
    {
        var owner = await ResolveAsync(Caller("subject-1"));

        await using (var first = _services.CreateAsyncScope())
        {
            (await first.ServiceProvider.GetRequiredService<IExternalIdentityService>()
                .DeleteAsync(owner, TestToken)).IsSuccess.ShouldBeTrue();
        }

        await using var second = _services.CreateAsyncScope();
        var again = await second.ServiceProvider.GetRequiredService<IExternalIdentityService>()
            .DeleteAsync(owner, TestToken);

        again.IsFailure.ShouldBeTrue();
        again.Error!.Code.ShouldBe("externalIdentity.mapping.notFound");
        _published.Events.OfType<UserAccountDeleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AnOwnerNoMappingNames_IsNotFoundAndPublishesNothing()
    {
        var unknown = UserId.Create(Guid.CreateVersion7());

        await using var scope = _services.CreateAsyncScope();
        var deletion = await scope.ServiceProvider.GetRequiredService<IExternalIdentityService>()
            .DeleteAsync(unknown, TestToken);

        deletion.IsFailure.ShouldBeTrue();
        deletion.Error!.Code.ShouldBe("externalIdentity.mapping.notFound");
        _published.Events.ShouldBeEmpty();
    }

    private static ExternalCallerDto Caller(string subject, string? email = null, string? name = null) =>
        new(_issuer, subject, email, name);

    private async Task<UserId> ResolveAsync(ExternalCallerDto caller)
    {
        await using var scope = _services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IExternalIdentityService>().ResolveAsync(caller, TestToken);
    }

    private async Task<List<Row>> RowsAsync()
    {
        await using var command = _keeper.CreateCommand();
        command.CommandText =
            "SELECT \"UserId\", \"Email\", \"DisplayName\", \"LastSeenAt\" FROM \"ExternalIdentities\"";

        await using var reader = await command.ExecuteReaderAsync(TestToken);
        List<Row> rows = [];

        while (await reader.ReadAsync(TestToken))
        {
            rows.Add(new Row(
                reader.GetGuid(0),
                await reader.IsDBNullAsync(1, TestToken) ? null : reader.GetString(1),
                await reader.IsDBNullAsync(2, TestToken) ? null : reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return rows;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _keeper.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestToken);
    }

    private sealed record Row(Guid UserId, string? Email, string? DisplayName, DateTimeOffset LastSeenAt);

    private sealed class SettableClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }

    private sealed class RivalInsertsFirst(IExternalIdentityStore inner, ExternalIdentityLink rival) : IExternalIdentityStore
    {
        private bool _lookedUp;

        public Task<ExternalIdentityLink?> FindAsync(string issuer, string subject, CancellationToken cancellationToken = default)
        {
            if (!_lookedUp)
            {
                _lookedUp = true;
                return Task.FromResult<ExternalIdentityLink?>(null);
            }

            return inner.FindAsync(issuer, subject, cancellationToken);
        }

        public async Task<bool> TryAddAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default)
        {
            (await inner.TryAddAsync(rival, cancellationToken)).ShouldBeTrue();

            return await inner.TryAddAsync(link, cancellationToken);
        }

        public Task RefreshAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default) =>
            inner.RefreshAsync(link, cancellationToken);

        public Task<int> RemoveAsync(UserId userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No provisioning test deletes an account.");
    }

    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        private readonly List<IDomainEvent> _events = [];

        public IReadOnlyList<IDomainEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(domainEvent);

            lock (_events)
            {
                _events.Add(domainEvent);
            }

            return Task.CompletedTask;
        }
    }
}
