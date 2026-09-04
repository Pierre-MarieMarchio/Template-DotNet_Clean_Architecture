using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common;

/// <summary>One call to <see cref="RecordingLogger{TCategoryName}"/>, already formatted.</summary>
internal sealed record LoggedEntry(LogLevel Level, string Message, Exception? Exception);
