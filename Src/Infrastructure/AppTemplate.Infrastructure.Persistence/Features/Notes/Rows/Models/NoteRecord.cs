namespace AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Models;

internal sealed class NoteRecord
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastModifiedAt { get; set; }

    public uint Version { get; set; }
}
