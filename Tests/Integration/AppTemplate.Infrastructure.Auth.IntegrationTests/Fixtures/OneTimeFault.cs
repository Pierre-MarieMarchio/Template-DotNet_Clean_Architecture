using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

public enum FaultPoint
{
    AfterTheConsumption,

    BeforeTheSuccessorsInsert,

    AfterTheCommit,
}

public sealed class OneTimeFault(FaultPoint point, bool transient)
{
    private int _fired;

    public IInterceptor[] Interceptors => [new Commands(this), new Commits(this)];

    public bool Fired => Volatile.Read(ref _fired) == 1;

    private void Strike(FaultPoint reached)
    {
        if (reached != point || Interlocked.Exchange(ref _fired, 1) == 1)
        {
            return;
        }

        throw transient
            ? DatabaseServer.TransientFault()
            : new InvalidOperationException("Injected: the statement failed.");
    }

    private static bool Names(DbCommand command, string verb) =>
        Regex.IsMatch(
            command.CommandText,
            $@"\b{verb}\b[^;]*RefreshTokens\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

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
