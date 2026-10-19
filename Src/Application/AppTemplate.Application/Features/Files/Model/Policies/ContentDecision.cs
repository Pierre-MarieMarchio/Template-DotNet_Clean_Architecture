namespace AppTemplate.Application.Features.Files.Model.Policies;

/// <summary>What <see cref="StoredFileContentPolicy"/> decided about one file's content.</summary>
public enum ContentDecision
{
    /// <summary>The content is acceptable and the file may be made available.</summary>
    Release,

    /// <summary>The content is refused. Terminal — nothing revisits it.</summary>
    Quarantine,

    /// <summary>
    /// No decision was reachable this time: the file stays put and is offered again. The only non-final outcome, so
    /// an outage is never taken for an approval or a refusal.
    /// </summary>
    Retry,
}
