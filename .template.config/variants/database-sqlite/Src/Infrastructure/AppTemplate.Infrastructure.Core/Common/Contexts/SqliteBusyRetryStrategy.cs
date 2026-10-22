using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage;

namespace AppTemplate.Infrastructure.Core.Common.Contexts;

internal sealed class SqliteBusyRetryStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10))
{
    private const int _busy = 5;

    private const int _locked = 6;

    protected override bool ShouldRetryOn(Exception exception) =>
        exception is SqliteException { SqliteErrorCode: _busy or _locked };
}
