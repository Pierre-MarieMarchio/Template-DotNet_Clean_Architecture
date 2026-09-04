namespace AppTemplate.Application.Features.Files.Policies;

/// <summary>What <see cref="StoredFileContentPolicy"/> decided about one file's content.</summary>
public enum ContentDecision
{
    /// <summary>The content is acceptable and the file may be made available.</summary>
    Release,

    /// <summary>The content is refused. Terminal — nothing revisits it.</summary>
    Quarantine,

    /// <summary>
    /// No decision was reachable this time. The file stays where it is and is offered again on the
    /// next pass. This is the only one of the three that is not final, and it exists so that an
    /// outage cannot be mistaken for either an approval or a refusal.
    /// </summary>
    Retry,
}
