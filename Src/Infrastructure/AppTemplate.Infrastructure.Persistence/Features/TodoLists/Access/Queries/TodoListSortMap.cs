using System.Globalization;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Policies;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Queries;

internal static class TodoListSortMap
{
    public static IOrderedQueryable<TodoListRecord> ApplyOrder(IQueryable<TodoListRecord> source, SortOrder sort)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sort);

        IOrderedQueryable<TodoListRecord>? ordered = null;

        foreach (var term in sort.Terms)
        {
            ordered = ordered is null ? OrderByTerm(source, term) : ThenByTerm(ordered, term);
        }

        return sort.Terms[^1].Direction == SortDirection.Ascending
            ? ordered!.ThenBy(record => record.Id)
            : ordered!.ThenByDescending(record => record.Id);
    }

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
        if (!DateTimeOffset.TryParse(
            cursor.Key,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset key))
        {
            throw new InvalidOperationException(
                "The cursor's key is not a valid date/time: CursorKeys should have rejected it "
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
