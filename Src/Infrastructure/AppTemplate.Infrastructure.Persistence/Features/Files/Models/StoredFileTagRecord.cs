namespace AppTemplate.Infrastructure.Persistence.Features.Files.Models;

/// <summary>
/// One tag on one file. Its primary key is <c>(StoredFileId, Value)</c>, which makes a duplicate tag
/// on a file unrepresentable in the database as well as in the domain.
/// <para>
/// Mapped as an ordinary entity rather than as an owned collection, for the reason
/// <see cref="TodoLists.Models.TodoItemTagRecord"/> states: reconciliation is something this layer
/// performs by hand, and implicit behaviour is what makes it hard to say which rows were written.
/// </para>
/// </summary>
internal sealed class StoredFileTagRecord
{
    public Guid StoredFileId { get; set; }

    public string Value { get; set; } = string.Empty;
}
