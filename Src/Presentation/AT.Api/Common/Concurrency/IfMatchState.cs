namespace AppTemplate.Api.Common.Concurrency;

internal enum IfMatchState
{
    Absent,

    Any,

    Tags,

    Malformed,
}
