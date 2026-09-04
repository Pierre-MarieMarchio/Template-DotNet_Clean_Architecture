namespace AppTemplate.Api.Common.Concurrency;

/// <summary>What a request's <c>If-Match</c> header amounts to.</summary>
internal enum IfMatchState
{
    /// <summary>No header. Whether that is allowed is policy, not syntax.</summary>
    Absent,

    /// <summary><c>*</c>: the resource must exist, whatever version it is at.</summary>
    Any,

    /// <summary>One or more entity tags.</summary>
    Tags,

    /// <summary>Present, but not <c>*</c> and not a list of quoted entity tags.</summary>
    Malformed,
}
