using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>A validated paging request, in either mode.</summary>
public sealed record PageRequest
{
    private PageRequest(PagingMode mode, int pageSize, int? page, Cursor? cursor)
    {
        Mode = mode;
        PageSize = pageSize;
        Page = page;
        Cursor = cursor;
    }

    /// <summary>
    /// Which of the two shapes this request is, since <see cref="Page"/> and <see cref="Cursor"/> are
    /// each set in one of them only.
    /// </summary>
    public PagingMode Mode { get; }

    /// <summary>
    /// Always resolved: a caller who sent none gets the policy's default. Never above the policy's
    /// maximum, which is refused rather than clamped, so a caller is told their number was ignored.
    /// </summary>
    public int PageSize { get; }

    /// <summary>1-based. Set only in <see cref="PagingMode.Offset"/>.</summary>
    public int? Page { get; }

    /// <summary><c>null</c> means the first page. Set only in <see cref="PagingMode.Cursor"/>.</summary>
    public Cursor? Cursor { get; }

    /// <summary>Parses the <c>paging</c> query parameter. Blank means <see cref="PagingMode.Offset"/>.</summary>
    public static Result<PagingMode> ParseMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Success(PagingMode.Offset);
        }

        if (string.Equals(raw, "offset", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(PagingMode.Offset);
        }

        if (string.Equals(raw, "cursor", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(PagingMode.Cursor);
        }

        return Result.Failure<PagingMode>(CollectionErrors.InvalidPaging(
            $"'{raw}' is not a valid paging mode. Use 'offset' or 'cursor'."));
    }

    /// <summary>
    /// The one place the two modes are checked against each other, so a request carrying both a page
    /// number and a cursor is refused instead of resolved by silently preferring one of them.
    /// </summary>
    /// <returns>
    /// A failure rather than an exception: bad paging is an answer the layer above turns into a 400.
    /// </returns>
    public static Result<PageRequest> Create(
        PagingMode mode,
        int? page,
        int? pageSize,
        Cursor? cursor,
        ICollectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        int size = pageSize ?? policy.DefaultPageSize;

        if (size < 1 || size > policy.MaxPageSize)
        {
            return Result.Failure<PageRequest>(CollectionErrors.InvalidPaging(
                $"'pageSize' must be between 1 and {policy.MaxPageSize}."));
        }

        if (mode == PagingMode.Offset)
        {
            if (cursor is not null)
            {
                return Result.Failure<PageRequest>(CollectionErrors.InvalidPaging(
                    "A cursor is only meaningful with paging=cursor."));
            }

            int pageNumber = page ?? 1;

            if (pageNumber < 1)
            {
                return Result.Failure<PageRequest>(CollectionErrors.InvalidPaging(
                    "'page' must be 1 or greater."));
            }

            return Result.Success(new PageRequest(mode, size, pageNumber, null));
        }

        if (page is not null)
        {
            return Result.Failure<PageRequest>(CollectionErrors.InvalidPaging(
                "'page' and a cursor are alternatives: send at most one."));
        }

        return Result.Success(new PageRequest(mode, size, null, cursor));
    }
}
