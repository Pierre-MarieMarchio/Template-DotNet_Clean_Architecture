using System.Globalization;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Policies;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;

/// <summary>
/// The whitelist's other half: everywhere <see cref="TodoListCollectionPolicy"/> names a field, this
/// is where that name becomes an actual column. No user string ever reaches an expression — every
/// method switches on <see cref="SortTerm.Field"/>, and the <c>default</c> arm of every switch
/// throws rather than silently ordering or filtering by nothing. The use case that builds
/// <see cref="TodoListCollectionPolicy"/>'s whitelist is supposed to have already rejected anything
/// not on the list, so reaching a <c>default</c> arm here is this template's own bug, not the
/// caller's, and it must stay loud.
/// </summary>
internal static class TodoListSortMap
{
    /// <summary>
    /// Folds every term into <c>ORDER BY</c>, then always appends <c>Id</c>. That tiebreaker is not
    /// optional: without a unique last key, two rows with equal sort keys can swap between pages, so
    /// one is served twice and another never.
    /// </summary>
    /// <remarks>
    /// The tiebreaker runs in the <em>same direction as the last term</em>, and that is load-bearing
    /// rather than cosmetic. <see cref="ApplyKeyset"/> resumes with
    /// <c>key = k AND Id &lt; id</c> when the term is descending, so a tiebreaker fixed ascending
    /// would order the tied rows one way and walk them the other — and every row tied on the sort key
    /// past the cursor would be skipped, silently, which is precisely the defect the tiebreaker
    /// exists to prevent. It also keeps the composite index usable: PostgreSQL can scan
    /// <c>(OwnerId, CreatedAt, Id)</c> backwards for <c>CreatedAt DESC, Id DESC</c>, but a mixed
    /// <c>CreatedAt DESC, Id ASC</c> would need an index declared with those directions.
    /// </remarks>
    public static IOrderedQueryable<TodoListRecord> ApplyOrder(IQueryable<TodoListRecord> source, SortOrder sort)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sort);

        IOrderedQueryable<TodoListRecord>? ordered = null;

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
    /// translates to the plain relational operators on both <c>text</c> and <c>uuid</c> in
    /// PostgreSQL.
    /// </summary>
    public static IQueryable<TodoListRecord> ApplyKeyset(IQueryable<TodoListRecord> source, SortTerm term, Cursor cursor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(cursor);

        return term.Field switch
        {
            TodoListCollectionPolicy.NameField => ApplyStringKeyset(source, term.Direction, cursor),
            TodoListCollectionPolicy.CreatedAtField => ApplyDateKeyset(source, term.Direction, cursor),
            _ => throw new InvalidOperationException(
                $"'{term.Field}' has no keyset comparison: the use case should have "
                + "rejected it before this was ever reached."),
        };
    }

    /// <summary>
    /// The value the next cursor carries, in the same wire form <see cref="Cursor.Key"/> expects,
    /// read from the last row actually served.
    /// </summary>
    /// <remarks>
    /// It reads the projected summary rather than a <see cref="TodoListRecord"/> on purpose. The
    /// keyset page never materialises an entity, so a record here could only be one assembled by
    /// hand from the projection — and a field made keyset-sortable later would then read a default
    /// off that half-filled object and mint a cursor pointing at the wrong row, silently. Reading
    /// the DTO makes the same mistake a compile error: a new keyset field has to appear on the
    /// summary before this switch can name it.
    /// </remarks>
    public static string KeyOf(TodoListSummaryDto summary, string field)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return field switch
        {
            TodoListCollectionPolicy.NameField => summary.Name,
            TodoListCollectionPolicy.CreatedAtField => summary.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException(
                $"'{field}' has no cursor key: the use case should have rejected it "
                + "before this was ever reached."),
        };
    }

    private static IOrderedQueryable<TodoListRecord> OrderByTerm(IQueryable<TodoListRecord> source, SortTerm term) =>
        term.Field switch
        {
            TodoListCollectionPolicy.NameField => term.Direction == SortDirection.Ascending
                ? source.OrderBy(record => record.Name)
                : source.OrderByDescending(record => record.Name),
            TodoListCollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                ? source.OrderBy(record => record.CreatedAt)
                : source.OrderByDescending(record => record.CreatedAt),
            TodoListCollectionPolicy.LastModifiedAtField => term.Direction == SortDirection.Ascending
                ? source.OrderBy(record => record.LastModifiedAt)
                : source.OrderByDescending(record => record.LastModifiedAt),
            _ => throw new InvalidOperationException(
                $"'{term.Field}' is not a sortable field: the use case should have "
                + "rejected it before a query was ever built."),
        };

    private static IOrderedQueryable<TodoListRecord> ThenByTerm(IOrderedQueryable<TodoListRecord> source, SortTerm term) =>
        term.Field switch
        {
            TodoListCollectionPolicy.NameField => term.Direction == SortDirection.Ascending
                ? source.ThenBy(record => record.Name)
                : source.ThenByDescending(record => record.Name),
            TodoListCollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                ? source.ThenBy(record => record.CreatedAt)
                : source.ThenByDescending(record => record.CreatedAt),
            TodoListCollectionPolicy.LastModifiedAtField => term.Direction == SortDirection.Ascending
                ? source.ThenBy(record => record.LastModifiedAt)
                : source.ThenByDescending(record => record.LastModifiedAt),
            _ => throw new InvalidOperationException(
                $"'{term.Field}' is not a sortable field: the use case should have "
                + "rejected it before a query was ever built."),
        };

    private static IQueryable<TodoListRecord> ApplyStringKeyset(
        IQueryable<TodoListRecord> source,
        SortDirection direction,
        Cursor cursor) =>
        direction == SortDirection.Ascending
            ? source.Where(record =>
                record.Name.CompareTo(cursor.Key) > 0
                || (record.Name == cursor.Key && record.Id.CompareTo(cursor.Id) > 0))
            : source.Where(record =>
                record.Name.CompareTo(cursor.Key) < 0
                || (record.Name == cursor.Key && record.Id.CompareTo(cursor.Id) < 0));

    private static IQueryable<TodoListRecord> ApplyDateKeyset(
        IQueryable<TodoListRecord> source,
        SortDirection direction,
        Cursor cursor)
    {
        // TodoListCursorKeys validates that a "createdAt" cursor's key parses as a date before the
        // use case ever calls this port, so an unparseable key here means that check was bypassed —
        // a defect in this template, not in the request, and it must stay loud rather than silently
        // ordering by nothing.
        if (!DateTimeOffset.TryParse(
            cursor.Key,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset key))
        {
            throw new InvalidOperationException(
                "The cursor's key is not a valid date/time: TodoListCursorKeys should have rejected it "
                + "before this was ever reached.");
        }

        return direction == SortDirection.Ascending
            ? source.Where(record =>
                record.CreatedAt.CompareTo(key) > 0
                || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) > 0))
            : source.Where(record =>
                record.CreatedAt.CompareTo(key) < 0
                || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) < 0));
    }
}
