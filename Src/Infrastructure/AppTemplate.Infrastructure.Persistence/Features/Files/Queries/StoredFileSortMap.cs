using System.Globalization;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Queries;

/// <summary>
/// The whitelist's other half: everywhere <see cref="StoredFileCollectionPolicy"/> names a field, this
/// is where that name becomes an actual column. No user string ever reaches an expression — every
/// method switches on <see cref="SortTerm.Field"/>, and the <c>default</c> arm of every switch throws
/// rather than silently ordering or filtering by nothing. <c>GetStoredFilesRequestBinder</c> is
/// supposed to have already rejected anything not on the list, so reaching a <c>default</c> arm here is
/// this template's own bug, not the caller's, and it must stay loud.
/// </summary>
internal static class StoredFileSortMap
{
    /// <summary>
    /// Folds every term into <c>ORDER BY</c>, then always appends <c>Id</c>. That tiebreaker is not
    /// optional: without a unique last key, two rows with equal sort keys can swap between pages, so
    /// one is served twice and another never.
    /// </summary>
    /// <remarks>
    /// The tiebreaker runs in the <em>same direction as the last term</em>, and that is load-bearing
    /// rather than cosmetic. <see cref="ApplyKeyset"/> resumes with <c>key = k AND Id &lt; id</c> when
    /// the term is descending, so a tiebreaker fixed ascending would order the tied rows one way and
    /// walk them the other — and every row tied on the sort key past the cursor would be skipped,
    /// silently. It also keeps the composite index usable: PostgreSQL can scan
    /// <c>(OwnerId, RegisteredAt, Id)</c> backwards for <c>RegisteredAt DESC, Id DESC</c>, which is the
    /// feature's default sort, but a mixed direction would need an index declared with those
    /// directions.
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

        // sort.Terms is never empty — SortOrder's own invariant — so ordered is always assigned here.
        return sort.Terms[^1].Direction == SortDirection.Ascending
            ? ordered!.ThenBy(record => record.Id)
            : ordered!.ThenByDescending(record => record.Id);
    }

    /// <summary>
    /// The keyset row comparison: <c>key &gt; k || (key == k &amp;&amp; Id &gt; id)</c> ascending, the
    /// mirror descending. <c>CompareTo</c> rather than <c>&lt;</c>/<c>&gt;</c> because it is what
    /// translates to the plain relational operators on both <c>text</c> and <c>uuid</c> in PostgreSQL.
    /// <para>
    /// <see cref="StoredFileCollectionPolicy.AvailableAtField"/> has no arm here and must not gain one
    /// while its column is nullable: a comparison against <c>NULL</c> is neither true nor false, so the
    /// row the cursor was minted from would be skipped instead of resumed from. The policy declares it
    /// <see cref="SortableField.OffsetOnly"/>, and that refusal happens before this is reached.
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

    /// <summary>
    /// The value the next cursor carries, in the same wire form <see cref="Cursor.Key"/> expects, read
    /// from the last row actually served.
    /// </summary>
    /// <remarks>
    /// It reads the projected DTO rather than a <see cref="StoredFileRecord"/> on purpose. The keyset
    /// page never materialises an entity, so a record here could only be one assembled by hand from the
    /// projection — and a field made keyset-sortable later would then read a default off that
    /// half-filled object and mint a cursor pointing at the wrong row, silently. Reading the DTO makes
    /// the same mistake a compile error.
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
        // GetStoredFilesCursorKeys validates that a "registeredAt" cursor's key parses as a date before
        // the use case ever calls this port, so an unparseable key here means that check was bypassed —
        // a defect in this template, not in the request, and it must stay loud rather than silently
        // ordering by nothing.
        if (!DateTimeOffset.TryParse(
            cursor.Key,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset key))
        {
            throw new InvalidOperationException(
                "The cursor's key is not a valid date/time: GetStoredFilesCursorKeys should have rejected "
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
