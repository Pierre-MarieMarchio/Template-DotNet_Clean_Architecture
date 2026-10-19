namespace AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Models;

/// <summary>
/// How a note is stored, and the only model it has: there is no aggregate and no mapper, so the
/// store reads and writes this row directly and projects it to DTOs.
/// <para>
/// It is not <c>IAuditable</c>: the two instants it keeps are set by the service that owns the
/// operation, so the audit interceptor, which the business context attaches for the other features'
/// rows, has nothing here to stamp.
/// </para>
/// </summary>
internal sealed class NoteRecord
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastModifiedAt { get; set; }

    /// <summary>
    /// PostgreSQL's <c>xmin</c> system column. Never written by this process: the database moves it on
    /// every <c>UPDATE</c>, EF reads it back, and the value goes into the <c>WHERE</c> clause of the
    /// next write.
    /// </summary>
    public uint Version { get; set; }
}
