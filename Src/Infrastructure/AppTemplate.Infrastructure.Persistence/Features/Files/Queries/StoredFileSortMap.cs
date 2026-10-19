using System.Globalization;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Queries;

/// <summary>
/// Maps the field names <see cref="StoredFileCollectionPolicy"/> whitelists onto columns. No user
/// string reaches an expression, and every <c>default</c> arm throws: the binder has already
/// rejected anything else, so reaching one is a defect here and must stay loud.
/// </summary>
internal static class StoredFileSortMap
{
    /// <summary>Folds every term into <c>ORDER BY</c>, then appends <c>Id</c> as a unique tiebreaker, so equal sort keys cannot swap between pages.</summary>
    /// <remarks>
    /// The tiebreaker runs in the direction of the last term: <see cref="ApplyKeyset"/> resumes with
    /// <c>Id &lt; id</c> when descending, so a fixed ascending tiebreaker would skip every tied row
    /// past the cursor. It also keeps the <c>(OwnerId, RegisteredAt, Id)</c> index scannable backwards.
    /// </remarks>
    public static IOrderedQueryable<StoredFileRecord> ApplyOrder(IQueryable<StoredFileRecord> source, SortOrder sort)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sort);

        IOrderedQueryable<StoredFileRecord>? ordered = null;

        foreach (var term in sort.Terms)
        {
            ordered = ordered is null ? OrderByTerm(source, term) : ThenByTerm(ordered, term);
        }

        // sort.Terms is never empty (SortOrder's invariant), so ordered is assigned.
        return sort.Terms[^1].Direction == SortDirection.Ascending
            ? ordered!.ThenBy(record => record.Id)
            : ordered!.ThenByDescending(record => record.Id);
    }

    /// <summary>
    /// The keyset row comparison, ascending or the mirror. <c>CompareTo</c> because it translates to
    /// plain relational operators on <c>text</c> and <c>uuid</c>.
    /// <para>
    /// <see cref="StoredFileCollectionPolicy.AvailableAtField"/> has no arm and must not gain one while
    /// its column is nullable: a comparison with <c>NULL</c> would skip the cursor's own row. The policy
    /// declares it <see cref="SortableField.OffsetOnly"/>.
    /// </para>
    /// </summary>
    public static IQueryable<StoredFileRecord> ApplyKeyset(
        IQueryable<StoredFileRecord> source,
        SortTerm term,
        Cursor cursor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(cursor);

        return term.Field switch
        {
            StoredFileCollectionPolicy.NameField => ApplyStringKeyset(source, term.Direction, cursor),
            StoredFileCollectionPolicy.RegisteredAtField => ApplyDateKeyset(source, term.Direction, cursor),
            _ => throw new InvalidOperationException(
                $"'{term.Field}' has no keyset comparison: the use case should have "
                + "rejected it before this was ever reached."),
        };
    }

    /// <summary>The value the next cursor carries, read from the last row served.</summary>
    /// <remarks>
    /// It reads the projected DTO, not a record: the keyset page never materialises an entity, and a
    /// hand-assembled record would mint a cursor from default values if a field became keyset-sortable.
    /// </remarks>
    public static string KeyOf(StoredFileDto file, string field)
    {
        ArgumentNullException.ThrowIfNull(file);

        return field switch
        {
            StoredFileCollectionPolicy.NameField => file.Name,
            StoredFileCollectionPolicy.RegisteredAtField =>
                file.RegisteredAt.ToString("O", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException(
                $"'{field}' has no cursor key: the use case should have rejected it "
                + "before this was ever reached."),
        };
    }

    private static IOrderedQueryable<StoredFileRecord> OrderByTerm(
        IQueryable<StoredFileRecord> source,
        SortTerm term) =>
        term.Field switch
        {
            StoredFileCollectionPolicy.NameField => term.Direction == SortDirection.Ascending
                ? source.OrderBy(record => record.Name)
                : source.OrderByDescending(record => record.Name),
            StoredFileCollectionPolicy.RegisteredAtField => term.Direction == SortDirection.Ascending
                ? source.OrderBy(record => record.RegisteredAt)
                : source.OrderByDescending(record => record.RegisteredAt),
            StoredFileCollectionPolicy.AvailableAtField => term.Direction == SortDirection.Ascending
                ? source.OrderBy(record => record.AvailableAt)
                : source.OrderByDescending(record => record.AvailableAt),
            _ => throw new InvalidOperationException(
                $"'{term.Field}' is not a sortable field: the use case should have "
                + "rejected it before a query was ever built."),
        };

    private static IOrderedQueryable<StoredFileRecord> ThenByTerm(
        IOrderedQueryable<StoredFileRecord> source,
        SortTerm term) =>
        term.Field switch
        {
            StoredFileCollectionPolicy.NameField => term.Direction == SortDirection.Ascending
                ? source.ThenBy(record => record.Name)
                : source.ThenByDescending(record => record.Name),
            StoredFileCollectionPolicy.RegisteredAtField => term.Direction == SortDirection.Ascending
                ? source.ThenBy(record => record.RegisteredAt)
                : source.ThenByDescending(record => record.RegisteredAt),
            StoredFileCollectionPolicy.AvailableAtField => term.Direction == SortDirection.Ascending
                ? source.ThenBy(record => record.AvailableAt)
                : source.ThenByDescending(record => record.AvailableAt),
            _ => throw new InvalidOperationException(
                $"'{term.Field}' is not a sortable field: the use case should have "
                + "rejected it before a query was ever built."),
        };

    private static IQueryable<StoredFileRecord> ApplyStringKeyset(
        IQueryable<StoredFileRecord> source,
        SortDirection direction,
        Cursor cursor) =>
        direction == SortDirection.Ascending
            ? source.Where(record =>
                record.Name.CompareTo(cursor.Key) > 0
                || (record.Name == cursor.Key && record.Id.CompareTo(cursor.Id) > 0))
            : source.Where(record =>
                record.Name.CompareTo(cursor.Key) < 0
                || (record.Name == cursor.Key && record.Id.CompareTo(cursor.Id) < 0));

    private static IQueryable<StoredFileRecord> ApplyDateKeyset(
        IQueryable<StoredFileRecord> source,
        SortDirection direction,
        Cursor cursor)
    {
        // CursorKeys has already validated the key, so a failure here is a defect and stays loud.
        if (!DateTimeOffset.TryParse(
            cursor.Key,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset key))
        {
            throw new InvalidOperationException(
                "The cursor's key is not a valid date/time: CursorKeys should have rejected "
                + "it before this was ever reached.");
        }

        return direction == SortDirection.Ascending
            ? source.Where(record =>
                record.RegisteredAt.CompareTo(key) > 0
                || (record.RegisteredAt == key && record.Id.CompareTo(cursor.Id) > 0))
            : source.Where(record =>
                record.RegisteredAt.CompareTo(key) < 0
                || (record.RegisteredAt == key && record.Id.CompareTo(cursor.Id) < 0));
    }
}
