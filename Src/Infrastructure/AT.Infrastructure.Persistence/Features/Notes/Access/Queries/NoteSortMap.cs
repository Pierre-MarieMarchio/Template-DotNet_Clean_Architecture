using System.Globalization;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Model.Policies;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Notes.Access.Queries;

internal static class NoteSortMap
{
    public static IOrderedQueryable<NoteRecord> ApplyOrder(IQueryable<NoteRecord> source, SortOrder sort)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sort);

        IOrderedQueryable<NoteRecord>? ordered = null;

        foreach (var term in sort.Terms)
        {
            ordered = ordered is null ? OrderByTerm(source, term) : ThenByTerm(ordered, term);
        }

        return sort.Terms[^1].Direction == SortDirection.Ascending
            ? ordered!.ThenBy(record => record.Id)
            : ordered!.ThenByDescending(record => record.Id);
    }

    public static IQueryable<NoteRecord> ApplyKeyset(IQueryable<NoteRecord> source, SortTerm term, Cursor cursor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(cursor);

        return term.Field switch
        {
            NoteCollectionPolicy.TitleField => ApplyTitleKeyset(source, term.Direction, cursor),
            NoteCollectionPolicy.CreatedAtField => ApplyCreatedAtKeyset(source, term.Direction, cursor),
            _ => throw new InvalidOperationException(
                $"'{term.Field}' has no keyset comparison: the policy should have rejected it first."),
        };
    }

    public static string KeyOf(NoteSummaryDto summary, string field)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return field switch
        {
            NoteCollectionPolicy.TitleField => summary.Title,
            NoteCollectionPolicy.CreatedAtField => summary.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException(
                $"'{field}' has no cursor key: the policy should have rejected it first."),
        };
    }

    private static IOrderedQueryable<NoteRecord> OrderByTerm(IQueryable<NoteRecord> source, SortTerm term) =>
        term.Field switch
        {
            NoteCollectionPolicy.TitleField => term.Direction == SortDirection.Ascending
                ? source.OrderBy(record => record.Title)
                : source.OrderByDescending(record => record.Title),
            NoteCollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                ? source.OrderBy(record => record.CreatedAt)
                : source.OrderByDescending(record => record.CreatedAt),
            _ => throw new InvalidOperationException(
                $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
        };

    private static IOrderedQueryable<NoteRecord> ThenByTerm(IOrderedQueryable<NoteRecord> source, SortTerm term) =>
        term.Field switch
        {
            NoteCollectionPolicy.TitleField => term.Direction == SortDirection.Ascending
                ? source.ThenBy(record => record.Title)
                : source.ThenByDescending(record => record.Title),
            NoteCollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                ? source.ThenBy(record => record.CreatedAt)
                : source.ThenByDescending(record => record.CreatedAt),
            _ => throw new InvalidOperationException(
                $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
        };

    private static IQueryable<NoteRecord> ApplyTitleKeyset(
        IQueryable<NoteRecord> source,
        SortDirection direction,
        Cursor cursor) =>
        direction == SortDirection.Ascending
            ? source.Where(record =>
                record.Title.CompareTo(cursor.Key) > 0
                || (record.Title == cursor.Key && record.Id.CompareTo(cursor.Id) > 0))
            : source.Where(record =>
                record.Title.CompareTo(cursor.Key) < 0
                || (record.Title == cursor.Key && record.Id.CompareTo(cursor.Id) < 0));

    private static IQueryable<NoteRecord> ApplyCreatedAtKeyset(
        IQueryable<NoteRecord> source,
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
                "The cursor's key is not a valid date/time: CursorKeys should have rejected it first.");
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
