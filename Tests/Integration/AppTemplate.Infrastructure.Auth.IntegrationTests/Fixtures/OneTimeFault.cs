using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

/// <summary>Where a <see cref="OneTimeFault"/> strikes.</summary>
public enum FaultPoint
{
    /// <summary>After the conditional <c>UPDATE</c> that consumes a grant has run: its acknowledgement is lost.</summary>
    AfterTheConsumption,

    /// <summary>Before the successor's <c>INSERT</c> runs.</summary>
    BeforeTheSuccessorsInsert,

    /// <summary>After a transaction committed: the commit's acknowledgement is lost.</summary>
    AfterTheCommit,
}

/// <summary>
/// Fails the identity context once, at one point of a rotation, and then lets everything through.
/// A transient fault is what the context's retrying execution strategy replays; any other one
/// reaches the caller.
/// </summary>
public sealed class OneTimeFault(FaultPoint point, bool transient)
{
    private int _fired;

    /// <summary>The interceptors to attach to the context, through <see cref="GrantTableFixture.ComposeWith"/>.</summary>
    public IInterceptor[] Interceptors => [new Commands(this), new Commits(this)];

    /// <summary>Whether the fault has struck.</summary>
    public bool Fired => Volatile.Read(ref _fired) == 1;

    private void Strike(FaultPoint reached)
    {
        if (reached != point || Interlocked.Exchange(ref _fired, 1) == 1)
        {
            return;
        }

        // Npgsql reports a broken connection as an NpgsqlException wrapping the I/O failure; that
        // inner exception is what makes it transient, and so retried.
        throw transient
            ? new NpgsqlException("Injected: the connection was lost.", new TimeoutException())
            : new InvalidOperationException("Injected: the statement failed.");
    }

    private static bool Names(DbCommand command, string verb) =>
        command.CommandText.TrimStart().StartsWith(verb, StringComparison.OrdinalIgnoreCase)
        && command.CommandText.Contains("\"RefreshTokens\"", StringComparison.Ordinal);

    private sealed class Commands(OneTimeFault fault) : DbCommandInterceptor
    {
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Names(command, "UPDATE"))
            {
                fault.Strike(FaultPoint.AfterTheConsumption);
            }

            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Names(command, "INSERT"))
            {
                fault.Strike(FaultPoint.BeforeTheSuccessorsInsert);
            }

            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Names(command, "INSERT"))
            {
                fault.Strike(FaultPoint.BeforeTheSuccessorsInsert);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class Commits(OneTimeFault fault) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            fault.Strike(FaultPoint.AfterTheCommit);

            return Task.CompletedTask;
        }
    }
}
