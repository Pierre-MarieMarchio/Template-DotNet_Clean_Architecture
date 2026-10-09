//#if (todoLists)
using System.Globalization;
//#endif
using AppTemplate.Domain.Core.Common.Events;
using Microsoft.Data.Sqlite;

namespace AppTemplate.Api.UnitTests.Composition;

/// <summary>One delivery: the event, and whether its list was committed when it arrived.</summary>
internal sealed record Publication(IDomainEvent Event, bool ListWasCommitted);

/// <summary>
/// What the consumers of <see cref="SqliteSavingHost"/> received. Whether the list was committed is
/// read over a connection of its own, so it sees only what some transaction has actually committed,
/// not what a context has staged.
/// </summary>
internal sealed class PublishedEvents(string businessDatabase)
{
    private readonly Lock _gate = new();
    private readonly List<Publication> _publications = [];

    public IReadOnlyList<Publication> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _publications];
            }
        }
    }

    public IReadOnlyList<TEvent> OfType<TEvent>()
        where TEvent : IDomainEvent => [.. All.Select(publication => publication.Event).OfType<TEvent>()];

    //#if (todoLists)
    public async Task RecordAsync(
        IDomainEvent domainEvent,
        Guid todoListId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(businessDatabase);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"TodoLists\" WHERE \"Id\" = $id";

        // EF stores a Guid as upper-case text on SQLite.
        command.Parameters.AddWithValue("$id", todoListId.ToString().ToUpperInvariant());

        object? count = await command.ExecuteScalarAsync(cancellationToken);
        bool committed = Convert.ToInt64(count, CultureInfo.InvariantCulture) > 0;

        lock (_gate)
        {
            _publications.Add(new Publication(domainEvent, committed));
        }
    }
    //#endif

    public async Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(businessDatabase);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
