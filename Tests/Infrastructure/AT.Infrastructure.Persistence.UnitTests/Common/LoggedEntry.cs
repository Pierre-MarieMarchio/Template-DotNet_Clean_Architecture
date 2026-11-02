using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common;

internal sealed record LoggedEntry(LogLevel Level, string Message, Exception? Exception);
